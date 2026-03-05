namespace SmartAssistant.Api.Services.Email
{
    public record EmailMessage(
        string Provider,
        string Id,
        string Subject,
        string Snippet,
        DateTimeOffset ReceivedOn,
        string From
    );

    public interface IEmailClient
    {
        Task<IReadOnlyList<EmailMessage>> GetImportantEmailsAsync(DateTimeOffset sinceUtc, CancellationToken ct, string gmailQuery);

        // New: Fetch one email again later (needed for pending replies)
        Task<EmailMessage?> GetEmailByIdAsync(string messageId, CancellationToken ct);

        // New: Send a reply
        Task ReplyAsync(string messageId, string body, CancellationToken ct);
    }

    // Stub: replace with Gmail API / Graph later
    public class FakeEmailClient : IEmailClient
    {
        public Task<IReadOnlyList<EmailMessage>> GetImportantEmailsAsync(DateTimeOffset sinceUtc, CancellationToken ct, string gmailQuery)
        {
            IReadOnlyList<EmailMessage> list =
            [
                new EmailMessage("Gmail", "msg_123", "Interview schedule", "Can you confirm time?", DateTimeOffset.UtcNow.AddMinutes(-5), "boss@company.com")
            ];

            return Task.FromResult(list);
        }

        public Task ReplyAsync(string messageId, string body, CancellationToken ct)
        {
            // Fake: do nothing
            return Task.CompletedTask;
        }

        public Task<EmailMessage?> GetEmailByIdAsync(string messageId, CancellationToken ct)
        {
            // Fake data for testing
            return Task.FromResult<EmailMessage?>(new EmailMessage(
                "Gmail",
                messageId,
                "Interview schedule",
                "Please confirm your availability.",
                DateTimeOffset.UtcNow,
                "boss@company.com"
            ));
        }
    }
}