using Google.Apis.Auth.OAuth2;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SmartAssistant.Api.Data;
using SmartAssistant.Api.Options;
using SmartAssistant.Core.Entities;
using System.Text.Json;

namespace SmartAssistant.Api.Services.Google
{
    public interface IOAuthTokenHelper
    {
        Task<string> GetAccessTokenAsync(string refreshToken, CancellationToken ct);
        Task<GoogleCredential> GetGoogleCredentialAsync(CancellationToken ct);
        Task<EmailOAuthAccount?> GetLatestActiveGmailAccountAsync(CancellationToken ct);
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

        public async Task<EmailOAuthAccount?> GetLatestActiveGmailAccountAsync(CancellationToken ct)
        {
            return await _db.EmailOAuthAccounts
                .Where(account =>
                    account.Active &&
                    account.Provider == "Gmail")
                .OrderByDescending(account => account.Id)
                .FirstOrDefaultAsync(ct);
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

            var json = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                var tokenError = TryDeserializeTokenError(json);

                if (tokenError != null &&
                    string.Equals(tokenError.error, "invalid_grant", StringComparison.OrdinalIgnoreCase))
                {
                    throw new GoogleOAuthReconnectRequiredException(
                        "Google refresh token is expired or revoked. User must reconnect Gmail account.");
                }

                throw new InvalidOperationException(
                    "Google token refresh failed. Status: " + (int)response.StatusCode + ". Response: " + json);
            }

            var token = JsonSerializer.Deserialize<TokenResponse>(json);

            if (token == null || string.IsNullOrWhiteSpace(token.access_token))
                throw new InvalidOperationException("Google token exchange failed.");

            return token.access_token;
        }

        public async Task<GoogleCredential> GetGoogleCredentialAsync(CancellationToken ct)
        {
            var account = await GetLatestActiveGmailAccountAsync(ct);

            if (account == null)
            {
                throw new GoogleOAuthReconnectRequiredException(
                    "No active Gmail OAuth account found. User must connect Gmail account.");
            }

            if (account.NeedsReconnect)
            {
                throw new GoogleOAuthReconnectRequiredException("Gmail account needs reconnect.");
            }

            if (string.IsNullOrWhiteSpace(account.RefreshToken))
            {
                throw new InvalidOperationException("Active Gmail account does not have a refresh token.");
            }

            try
            {
                var accessToken = await GetAccessTokenAsync(account.RefreshToken, ct);

                if (!string.IsNullOrWhiteSpace(account.LastError) || account.ReconnectRequiredOn.HasValue)
                {
                    account.LastError = null;
                    account.ReconnectRequiredOn = null;
                    account.UpdatedOn = DateTimeOffset.UtcNow;

                    await _db.SaveChangesAsync(ct);
                }

                return GoogleCredential.FromAccessToken(accessToken);
            }
            catch (GoogleOAuthReconnectRequiredException ex)
            {
                account.NeedsReconnect = true;
                account.LastError = ex.Message;
                account.ReconnectRequiredOn = DateTimeOffset.UtcNow;
                account.UpdatedOn = DateTimeOffset.UtcNow;

                await _db.SaveChangesAsync(ct);

                throw;
            }
            catch (Exception ex)
            {
                account.LastError = ex.Message;
                account.UpdatedOn = DateTimeOffset.UtcNow;

                await _db.SaveChangesAsync(ct);

                throw;
            }
        }

        private static TokenErrorResponse? TryDeserializeTokenError(string json)
        {
            try
            {
                return JsonSerializer.Deserialize<TokenErrorResponse>(json);
            }
            catch
            {
                return null;
            }
        }

        private sealed class TokenResponse
        {
            public string? access_token { get; set; }
        }

        private sealed class TokenErrorResponse
        {
            public string? error { get; set; }
            public string? error_description { get; set; }
        }
    }
}