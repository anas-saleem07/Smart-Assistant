using Google.Apis.Auth.OAuth2;
using Google.Apis.Gmail.v1;
using Google.Apis.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SmartAssistant.Api.Data;
using SmartAssistant.Api.Options;
using SmartAssistant.Core.Entities;

namespace SmartAssistant.Api.Services.Email
{
    public class GmailEmailClient : IEmailClient
    {
        private readonly ApplicationDbContext _db;
        private readonly GmailOAuthOptions _options;

        public GmailEmailClient(
            ApplicationDbContext db,
            IOptions<GmailOAuthOptions> options)
        {
            _db = db;
            _options = options.Value;
        }

        public async Task<IReadOnlyList<EmailMessage>> GetImportantEmailsAsync(
            DateTimeOffset sinceUtc,
            CancellationToken ct, string gmailQuery)
        {
            // 1️⃣ Get latest active Gmail OAuth account
            var account = await _db.EmailOAuthAccounts
                .Where(x => x.Provider == "Gmail" && x.Active)
                .OrderByDescending(x => x.Id)
                .FirstOrDefaultAsync(ct);

            if (account == null)
                return [];

            // 2️⃣ Exchange RefreshToken → AccessToken
            var accessToken = await ExchangeRefreshTokenAsync(account.RefreshToken, ct);

            var credential = GoogleCredential
                .FromAccessToken(accessToken);

            var gmail = new GmailService(new BaseClientService.Initializer
            {
                HttpClientInitializer = credential,
                ApplicationName = "SmartAssistant"
            });

            // 3️⃣ Fetch important emails
            var request = gmail.Users.Messages.List("me");
            request.Q = string.IsNullOrWhiteSpace(gmailQuery)? "in:inbox newer_than:7d": gmailQuery;

            var response = await request.ExecuteAsync(ct);

            if (response.Messages == null)
                return [];

            var results = new List<EmailMessage>();

            foreach (var msg in response.Messages.Take(20))
            {
                var full = await gmail.Users.Messages.Get("me", msg.Id)
                    .ExecuteAsync(ct);

                var subject = full.Payload?.Headers?
                    .FirstOrDefault(h => h.Name == "Subject")?.Value ?? "(no subject)";

                var from = full.Payload?.Headers?
                    .FirstOrDefault(h => h.Name == "From")?.Value ?? "(unknown)";

                var snippet = full.Snippet ?? "";

                var received = full.InternalDate.HasValue
                    ? DateTimeOffset.FromUnixTimeMilliseconds(full.InternalDate.Value)
                    : DateTimeOffset.UtcNow;

                if (received < sinceUtc)
                    continue;

                results.Add(new EmailMessage(
                    "Gmail",
                    msg.Id,
                    subject,
                    snippet,
                    received,
                    from));
            }

            return results;
        }

        private async Task<string> ExchangeRefreshTokenAsync(
            string refreshToken,
            CancellationToken ct)
        {
            using var http = new HttpClient();

            var values = new Dictionary<string, string>
            {
                { "client_id", _options.ClientId },
                { "client_secret", _options.ClientSecret },
                { "refresh_token", refreshToken },
                { "grant_type", "refresh_token" }
            };

            var response = await http.PostAsync(
                "https://oauth2.googleapis.com/token",
                new FormUrlEncodedContent(values),
                ct);

            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(ct);

            var token = System.Text.Json.JsonSerializer.Deserialize<TokenResponse>(json);

            return token!.access_token!;
        }

        private sealed class TokenResponse
        {
            public string? access_token { get; set; }
        }
    }
}