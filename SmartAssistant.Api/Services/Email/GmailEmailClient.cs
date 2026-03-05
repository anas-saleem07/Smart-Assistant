using Google.Apis.Auth.OAuth2;
using Google.Apis.Gmail.v1;
using Google.Apis.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SmartAssistant.Api.Data;
using SmartAssistant.Api.Options;
using SmartAssistant.Core.Entities;
using Google.Apis.Gmail.v1.Data;
using System.Text;

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

        public async Task ReplyAsync(string messageId, string body, CancellationToken ct)
        {
            // What: Send reply using Gmail API.
            // Why: Needs gmail.send scope and safe header handling.

            var account = await _db.EmailOAuthAccounts
                .Where(x => x.Provider == "Gmail" && x.Active)
                .OrderByDescending(x => x.Id)
                .FirstOrDefaultAsync(ct);

            if (account == null)
                throw new InvalidOperationException("No active Gmail OAuth account found.");

            var accessToken = await ExchangeRefreshTokenAsync(account.RefreshToken, ct);

            var credential = GoogleCredential.FromAccessToken(accessToken);

            var gmail = new GmailService(new BaseClientService.Initializer
            {
                HttpClientInitializer = credential,
                ApplicationName = "SmartAssistant"
            });

            Message original;

            try
            {
                original = await gmail.Users.Messages.Get("me", messageId).ExecuteAsync(ct);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Failed to load original email from Gmail. " + ex.Message, ex);
            }

            var headers = original.Payload?.Headers ?? new List<MessagePartHeader>();

            var fromHeader = GetHeader(headers, "From");
            var subjectHeader = GetHeader(headers, "Subject") ?? "";
            var messageIdHeader = GetHeader(headers, "Message-Id");

            if (string.IsNullOrWhiteSpace(fromHeader))
                throw new InvalidOperationException("Original email has no From header.");

            // Gmail "From" header can look like: "HR Team <hr@company.com>"
            // We need only the email part for "To:"
            var toAddress = ExtractEmailAddress(fromHeader);
            if (string.IsNullOrWhiteSpace(toAddress))
                throw new InvalidOperationException("Could not parse email address from From header: " + fromHeader);

            // We do not control Gmail UI "Re:" in conversations fully.
            // But we keep a safe subject header anyway.
            var replySubject = subjectHeader.StartsWith("Re:", StringComparison.OrdinalIgnoreCase)
                ? subjectHeader
                : "Re: " + subjectHeader;

            // Build RFC822 message
            var mime = new StringBuilder();
            mime.AppendLine("To: " + toAddress);
            mime.AppendLine("Subject: " + replySubject);

            if (!string.IsNullOrWhiteSpace(messageIdHeader))
            {
                mime.AppendLine("In-Reply-To: " + messageIdHeader);
                mime.AppendLine("References: " + messageIdHeader);
            }

            mime.AppendLine("Content-Type: text/plain; charset=\"UTF-8\"");
            mime.AppendLine();
            mime.AppendLine(body ?? "");

            var raw = Base64UrlEncode(Encoding.UTF8.GetBytes(mime.ToString()));

            var msg = new Message
            {
                Raw = raw
            };

            // ThreadId can be null in rare cases
            if (!string.IsNullOrWhiteSpace(original.ThreadId))
                msg.ThreadId = original.ThreadId;

            try
            {
                await gmail.Users.Messages.Send(msg, "me").ExecuteAsync(ct);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Gmail send failed. " + ex.Message, ex);
            }
        }

        private static string? GetHeader(IList<MessagePartHeader> headers, string name)
        {
            for (int i = 0; i < headers.Count; i++)
            {
                var h = headers[i];
                if (h != null && string.Equals(h.Name, name, StringComparison.OrdinalIgnoreCase))
                    return h.Value;
            }

            return null;
        }

        private static string ExtractEmailAddress(string fromHeader)
        {
            // Examples:
            // "HR Team <hr@company.com>" -> hr@company.com
            // "hr@company.com" -> hr@company.com

            var text = fromHeader.Trim();

            var lt = text.IndexOf('<');
            var gt = text.IndexOf('>');

            if (lt >= 0 && gt > lt)
            {
                var inside = text.Substring(lt + 1, gt - lt - 1).Trim();
                return inside;
            }

            // fallback: if it already looks like an email
            if (text.Contains("@"))
                return text;

            return "";
        }

        public async Task<EmailMessage?> GetEmailByIdAsync(string messageId, CancellationToken ct)
        {
            var account = await _db.EmailOAuthAccounts
                .Where(x => x.Provider == "Gmail" && x.Active)
                .OrderByDescending(x => x.Id)
                .FirstOrDefaultAsync(ct);

            if (account == null)
                return null;

            var accessToken = await ExchangeRefreshTokenAsync(account.RefreshToken, ct);

            var credential = GoogleCredential.FromAccessToken(accessToken);

            var gmail = new GmailService(new BaseClientService.Initializer
            {
                HttpClientInitializer = credential,
                ApplicationName = "SmartAssistant"
            });

            var full = await gmail.Users.Messages.Get("me", messageId)
                .ExecuteAsync(ct);

            var subject = full.Payload?.Headers?
                .FirstOrDefault(h => h.Name == "Subject")?.Value ?? "(no subject)";

            var from = full.Payload?.Headers?
                .FirstOrDefault(h => h.Name == "From")?.Value ?? "(unknown)";

            var snippet = full.Snippet ?? "";

            var received = full.InternalDate.HasValue
                ? DateTimeOffset.FromUnixTimeMilliseconds(full.InternalDate.Value)
                : DateTimeOffset.UtcNow;

            return new EmailMessage(
                "Gmail",
                messageId,
                subject,
                snippet,
                received,
                from
            );
        }

        private static string Base64UrlEncode(byte[] input)
        {
            return Convert.ToBase64String(input)
                .Replace("+", "-")
                .Replace("/", "_")
                .Replace("=", "");
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