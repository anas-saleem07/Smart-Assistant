using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SmartAssistant.Api.Data;
using SmartAssistant.Api.Options;
using SmartAssistant.Core.Entities;

public interface IEmailOAuthService
{
    string GenerateGoogleAuthUrl(string state);
    Task HandleGoogleCallbackAsync(string code, CancellationToken ct);
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

    public string GenerateGoogleAuthUrl(string state)
    {
        // What:
        // Request Gmail read + send + Calendar permissions.
        // Why:
        // We need to read emails, reply to them, and create calendar events.

        var scope =
            "openid email profile " +
            "https://www.googleapis.com/auth/gmail.readonly " +
            "https://www.googleapis.com/auth/gmail.send " +
            "https://www.googleapis.com/auth/calendar.events";

        return "https://accounts.google.com/o/oauth2/v2/auth" +
               $"?client_id={Uri.EscapeDataString(_options.ClientId)}" +
               $"&redirect_uri={Uri.EscapeDataString(_options.RedirectUri)}" +
               "&response_type=code" +
               $"&scope={Uri.EscapeDataString(scope)}" +
               $"&state={Uri.EscapeDataString(state)}" +
               "&access_type=offline" +
               "&prompt=consent";
    }

    // ... your GenerateGoogleAuthUrl stays same ...

    public async Task HandleGoogleCallbackAsync(string code, CancellationToken ct)
    {
        // 1) Exchange code -> access + refresh token
        var tokens = await ExchangeCodeForTokensAsync(code, ct);

        // 2) Fetch user info (name + email) using access token
        var user = await FetchGoogleUserInfoAsync(tokens.AccessToken, ct);

        // 3) Save account
        var account = new EmailOAuthAccount
        {
            Provider = "Gmail",
            Email = user.Email ?? "unknown",
            DisplayName = user.Name,
            RefreshToken = tokens.RefreshToken,   // IMPORTANT: refresh token for future scans/replies
            Active = true,
            UpdatedOn = DateTimeOffset.UtcNow
        };

        _db.EmailOAuthAccounts.Add(account);
        await _db.SaveChangesAsync(ct);
    }

    // -------------------------
    // Token exchange (FIX)
    // -------------------------
    private async Task<TokenResult> ExchangeCodeForTokensAsync(string code, CancellationToken ct)
    {
        using var http = new HttpClient();

        // Google token endpoint expects x-www-form-urlencoded
        var values = new Dictionary<string, string>
        {
            { "client_id", _options.ClientId },
            { "client_secret", _options.ClientSecret },
            { "code", code },
            { "redirect_uri", _options.RedirectUri },
            { "grant_type", "authorization_code" }
        };

        using var resp = await http.PostAsync(
            "https://oauth2.googleapis.com/token",
            new FormUrlEncodedContent(values),
            ct);

        var body = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
        {
            // This message is useful for debugging redirect_uri_mismatch etc.
            throw new InvalidOperationException(
                "Google token exchange failed. Status=" + (int)resp.StatusCode + ". Body=" + body);
        }

        var token = JsonSerializer.Deserialize<GoogleTokenResponse>(body);

        if (token == null || string.IsNullOrWhiteSpace(token.access_token))
            throw new InvalidOperationException("Google token exchange returned empty access_token.");

        // Refresh token might be null if user already consented and Google doesn't return it again.
        // For FYP, we require refresh token to run background jobs.
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

    // -------------------------
    // Userinfo (name/email)
    // -------------------------
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

        // Easy mapping
        public string? Email => email;
        public string? Name => name;
    }
}