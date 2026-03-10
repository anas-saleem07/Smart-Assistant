using Google.Apis.Auth.OAuth2;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SmartAssistant.Api.Data;
using SmartAssistant.Api.Options;
using System.Text.Json;

namespace SmartAssistant.Api.Services.Google
{
    public interface IOAuthTokenHelper
    {
        Task<string> GetAccessTokenAsync(string refreshToken, CancellationToken ct);
        Task<GoogleCredential> GetGoogleCredentialAsync(CancellationToken ct);
    }

    public sealed class OAuthTokenHelper : IOAuthTokenHelper
    {
        private readonly GmailOAuthOptions _opt;
        private readonly ApplicationDbContext _db;

        public OAuthTokenHelper(IOptions<GmailOAuthOptions> opt, ApplicationDbContext db)
        {
            _opt = opt.Value;
            _db = db;
        }

        public async Task<string> GetAccessTokenAsync(string refreshToken, CancellationToken ct)
        {
            using var http = new HttpClient();

            var values = new Dictionary<string, string>
            {
                { "client_id", _opt.ClientId },
                { "client_secret", _opt.ClientSecret },
                { "refresh_token", refreshToken },
                { "grant_type", "refresh_token" }
            };

            var response = await http.PostAsync(
                "https://oauth2.googleapis.com/token",
                new FormUrlEncodedContent(values),
                ct);

            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(ct);
            var token = JsonSerializer.Deserialize<TokenResponse>(json);

            if (token == null || string.IsNullOrWhiteSpace(token.access_token))
                throw new InvalidOperationException("Google token exchange failed.");

            return token.access_token;
        }

        public async Task<GoogleCredential> GetGoogleCredentialAsync(CancellationToken ct)
        {
            // Get latest active Gmail OAuth account
            var account = await _db.EmailOAuthAccounts
                .Where(x => x.Active && x.Provider == "Gmail")
                .OrderByDescending(x => x.Id)
                .FirstOrDefaultAsync(ct);

            if (account == null)
                throw new InvalidOperationException("No active Gmail OAuth account found.");

            if (string.IsNullOrWhiteSpace(account.RefreshToken))
                throw new InvalidOperationException("Active Gmail account does not have a refresh token.");

            var accessToken = await GetAccessTokenAsync(account.RefreshToken, ct);

            // Build Google credential from fresh access token
            var credential = GoogleCredential.FromAccessToken(accessToken);

            return credential;
        }

        private sealed class TokenResponse
        {
            public string? access_token { get; set; }
        }
    }
}