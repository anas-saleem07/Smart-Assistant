using Google.Apis.Auth.OAuth2;
using Google.Apis.Gmail.v1;
using Google.Apis.Gmail.v1.Data;
using Google.Apis.Services;
using SmartAssistant.Api.Services.Google;
using System.Text;

namespace SmartAssistant.Api.Services.Email
{
    public class GmailEmailClient : IEmailClient
    {
        private readonly IOAuthTokenHelper _oauthTokenHelper;

        public GmailEmailClient(IOAuthTokenHelper oauthTokenHelper)
        {
            _oauthTokenHelper = oauthTokenHelper;
        }

        public async Task<IReadOnlyList<EmailMessage>> GetImportantEmailsAsync(
            DateTimeOffset sinceUtc,
            CancellationToken ct,
            string? gmailQuery = null)
        {
            try
            {
                var credential = await _oauthTokenHelper.GetGoogleCredentialAsync(ct);

                var gmail = new GmailService(new BaseClientService.Initializer
                {
                    HttpClientInitializer = credential,
                    ApplicationName = "SmartAssistant"
                });

                var request = gmail.Users.Messages.List("me");
                request.Q = string.IsNullOrWhiteSpace(gmailQuery)
                    ? "in:inbox newer_than:7d"
                    : gmailQuery;

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
            catch (GoogleOAuthReconnectRequiredException)
            {
                // Why:
                // Gmail token is no longer usable, so inbox polling should fail gracefully.
                return [];
            }
        }

        public async Task ReplyAsync(string messageId, string body, CancellationToken ct)
        {
            try
            {
                var credential = await _oauthTokenHelper.GetGoogleCredentialAsync(ct);

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

                var toAddress = ExtractEmailAddress(fromHeader);
                if (string.IsNullOrWhiteSpace(toAddress))
                    throw new InvalidOperationException("Could not parse email address from From header: " + fromHeader);

                var replySubject = subjectHeader.StartsWith("Re:", StringComparison.OrdinalIgnoreCase)
                    ? subjectHeader
                    : "Re: " + subjectHeader;

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
            catch (GoogleOAuthReconnectRequiredException ex)
            {
                throw new InvalidOperationException("Gmail account needs reconnect before sending replies. " + ex.Message, ex);
            }
        }

        public async Task<EmailMessage?> GetEmailByIdAsync(string messageId, CancellationToken ct)
        {
            try
            {
                var credential = await _oauthTokenHelper.GetGoogleCredentialAsync(ct);

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
                    from);
            }
            catch (GoogleOAuthReconnectRequiredException)
            {
                return null;
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
            var text = fromHeader.Trim();

            var lt = text.IndexOf('<');
            var gt = text.IndexOf('>');

            if (lt >= 0 && gt > lt)
            {
                var inside = text.Substring(lt + 1, gt - lt - 1).Trim();
                return inside;
            }

            if (text.Contains("@"))
                return text;

            return "";
        }

        private static string Base64UrlEncode(byte[] input)
        {
            return Convert.ToBase64String(input)
                .Replace("+", "-")
                .Replace("/", "_")
                .Replace("=", "");
        }
    }
}