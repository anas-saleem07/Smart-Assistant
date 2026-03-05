using Google.Apis.Auth.OAuth2;
using Microsoft.Extensions.Options;
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

        public OAuthTokenHelper(IOptions<GmailOAuthOptions> opt)
        {
            _opt = opt.Value;
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
            // TODO: Replace with your actual way to build credential:
            // - Load stored refresh token / access token
            // - Refresh if needed
            // - Ensure Calendar scope exists

            await Task.CompletedTask;

            // Example placeholder to force you to wire real credentials:
            throw new System.NotImplementedException("Implement GoogleCredential creation here (with Calendar scope).");
        }
        private sealed class TokenResponse
        {
            public string? access_token { get; set; }
        }
    }
}