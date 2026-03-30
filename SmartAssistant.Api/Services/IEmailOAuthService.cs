using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SmartAssistant.Api.Data;
using SmartAssistant.Api.Options;
using SmartAssistant.Core.Entities;

public interface IEmailOAuthService
{
    string GenerateGoogleAuthUrl(string state, string platform);
    Task HandleGoogleCallbackAsync(string code, string platform, CancellationToken ct);
}

public class EmailOAuthService : IEmailOAuthService
{
    private readonly GmailOAuthOptions _options;
    private readonly ApplicationDbContext _db;

    public EmailOAuthService(IOptions<GmailOAuthOptions> options, ApplicationDbContext db)
    {
        _options = options.Value;
        _db = db;
    }

    public string GenerateGoogleAuthUrl(string state, string platform)
    {
        var redirectUri = GetRedirectUri(platform);

        if (string.IsNullOrWhiteSpace(_options.ClientId))
            throw new InvalidOperationException("GmailOAuth ClientId is missing.");

        if (string.IsNullOrWhiteSpace(redirectUri))
            throw new InvalidOperationException("RedirectUri is missing for platform: " + platform);

        var scope =
            "openid email profile " +
            "https://www.googleapis.com/auth/gmail.readonly " +
            "https://www.googleapis.com/auth/gmail.send " +
            "https://www.googleapis.com/auth/calendar.events";

        return "https://accounts.google.com/o/oauth2/v2/auth" +
               "?client_id=" + Uri.EscapeDataString(_options.ClientId) +
               "&redirect_uri=" + Uri.EscapeDataString(redirectUri) +
               "&response_type=code" +
               "&scope=" + Uri.EscapeDataString(scope) +
               "&state=" + Uri.EscapeDataString(state) +
               "&access_type=offline" +
               "&prompt=consent";
    }

    public async Task HandleGoogleCallbackAsync(string code, string platform, CancellationToken ct)
    {
        var redirectUri = GetRedirectUri(platform);

        var tokens = await ExchangeCodeForTokensAsync(code, redirectUri, ct);
        var user = await FetchGoogleUserInfoAsync(tokens.AccessToken, ct);

        var email = user.Email ?? "unknown";

        var existingAccount = await _db.EmailOAuthAccounts
            .Where(account => account.Provider == "Gmail" && account.Email == email)
            .OrderByDescending(account => account.Id)
            .FirstOrDefaultAsync(ct);

        if (existingAccount != null)
        {
            existingAccount.DisplayName = user.Name;
            existingAccount.RefreshToken = tokens.RefreshToken;
            existingAccount.Active = true;
            existingAccount.NeedsReconnect = false;
            existingAccount.LastError = null;
            existingAccount.ReconnectRequiredOn = null;
            existingAccount.UpdatedOn = DateTimeOffset.UtcNow;
        }
        else
        {
            var account = new EmailOAuthAccount
            {
                Provider = "Gmail",
                Email = email,
                DisplayName = user.Name,
                RefreshToken = tokens.RefreshToken,
                Active = true,
                NeedsReconnect = false,
                LastError = null,
                ReconnectRequiredOn = null,
                UpdatedOn = DateTimeOffset.UtcNow
            };

            _db.EmailOAuthAccounts.Add(account);
        }

        await _db.SaveChangesAsync(ct);
    }

    private string GetRedirectUri(string platform)
    {
        var redirectUri = string.Equals(platform, "android", StringComparison.OrdinalIgnoreCase)
            ? _options.AndroidRedirectUri
            : _options.WindowsRedirectUri;

        if (string.IsNullOrWhiteSpace(redirectUri))
            throw new InvalidOperationException("Google OAuth redirect URI is missing for platform: " + platform);

        return redirectUri;
    }

    private async Task<TokenResult> ExchangeCodeForTokensAsync(string code, string redirectUri, CancellationToken ct)
    {
        using var http = new HttpClient();

        var values = new Dictionary<string, string>
        {
            { "client_id", _options.ClientId },
            { "client_secret", _options.ClientSecret },
            { "code", code },
            { "redirect_uri", redirectUri },
            { "grant_type", "authorization_code" }
        };

        using var resp = await http.PostAsync(
            "https://oauth2.googleapis.com/token",
            new FormUrlEncodedContent(values),
            ct);

        var body = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                "Google token exchange failed. Status=" + (int)resp.StatusCode + ". Body=" + body);
        }

        var token = JsonSerializer.Deserialize<GoogleTokenResponse>(body);

        if (token == null || string.IsNullOrWhiteSpace(token.access_token))
            throw new InvalidOperationException("Google token exchange returned empty access_token.");

        if (string.IsNullOrWhiteSpace(token.refresh_token))
        {
            throw new InvalidOperationException(
                "Google did not return refresh_token. " +
                "Go to Google Account -> Security -> Third-party access and remove the app, then reconnect " +
                "OR ensure you use access_type=offline and prompt=consent.");
        }

        return new TokenResult
        {
            AccessToken = token.access_token!,
            RefreshToken = token.refresh_token!
        };
    }

    private sealed class GoogleTokenResponse
    {
        public string? access_token { get; set; }
        public string? refresh_token { get; set; }
        public int expires_in { get; set; }
        public string? token_type { get; set; }
        public string? scope { get; set; }
    }

    private sealed class TokenResult
    {
        public string AccessToken { get; set; } = "";
        public string RefreshToken { get; set; } = "";
    }

    private async Task<GoogleUserInfo> FetchGoogleUserInfoAsync(string accessToken, CancellationToken ct)
    {
        using var http = new HttpClient();

        var req = new HttpRequestMessage(HttpMethod.Get, "https://openidconnect.googleapis.com/v1/userinfo");
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

        using var resp = await http.SendAsync(req, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                "Failed to fetch Google userinfo. Status=" + (int)resp.StatusCode + ". Body=" + body);
        }

        var user = JsonSerializer.Deserialize<GoogleUserInfo>(body);

        return user ?? new GoogleUserInfo();
    }

    private sealed class GoogleUserInfo
    {
        public string? email { get; set; }
        public string? name { get; set; }

        public string? Email => email;
        public string? Name => name;
    }
}