using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SmartAssistant.Api.Data;
using SmartAssistant.Api.Options;
using SmartAssistant.Core.Entities;

public interface IEmailOAuthService
{
    string GenerateGoogleAuthUrl();
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

    public string GenerateGoogleAuthUrl()
    {
        return "https://accounts.google.com/o/oauth2/v2/auth" +
               $"?client_id={Uri.EscapeDataString(_options.ClientId)}" +
               $"&redirect_uri={Uri.EscapeDataString(_options.RedirectUri)}" +
               "&response_type=code" +
               "&scope=https%3A%2F%2Fwww.googleapis.com%2Fauth%2Fgmail.readonly" +
               "&access_type=offline" +
               "&prompt=consent";
    }

    public async Task HandleGoogleCallbackAsync(string code, CancellationToken ct)
    {
        using var http = new HttpClient();

        var values = new Dictionary<string, string>
        {
            { "client_id", _options.ClientId },
            { "client_secret", _options.ClientSecret },
            { "code", code },
            { "redirect_uri", _options.RedirectUri },
            { "grant_type", "authorization_code" }
        };

        var response = await http.PostAsync(
            "https://oauth2.googleapis.com/token",
            new FormUrlEncodedContent(values),
            ct);

        var json = await response.Content.ReadAsStringAsync(ct);
        response.EnsureSuccessStatusCode();

        var token = JsonSerializer.Deserialize<GoogleTokenResponse>(json);

        if (token == null || string.IsNullOrWhiteSpace(token.refresh_token))
        {
            throw new InvalidOperationException(
                "No refresh_token returned. Remove the app from Google Account access and connect again.");
        }

        // Save/update token in DB
        var existing = await _db.EmailOAuthAccounts
            .Where(x => x.Provider == "Gmail" && x.Active)
            .OrderByDescending(x => x.Id)
            .FirstOrDefaultAsync(ct);

        if (existing == null)
        {
            _db.EmailOAuthAccounts.Add(new EmailOAuthAccount
            {
                Provider = "Gmail",
                Email = "gmail-user",
                RefreshToken = token.refresh_token,
                Active = true
            });
        }
        else
        {
            existing.RefreshToken = token.refresh_token;
            existing.Active = true;
        }

        await _db.SaveChangesAsync(ct);
    }

    private sealed class GoogleTokenResponse
    {
        public string? access_token { get; set; }
        public string? refresh_token { get; set; }
        public int expires_in { get; set; }
        public string? token_type { get; set; }
        public string? scope { get; set; }
    }
}