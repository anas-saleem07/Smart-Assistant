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
        // “Important” can mean Gmail label IMPORTANT, or your own logic later
        Task<IReadOnlyList<EmailMessage>> GetImportantEmailsAsync(DateTimeOffset sinceUtc, CancellationToken ct);
    }

    // Stub: replace with Gmail API / Graph later
    public class FakeEmailClient : IEmailClient
    {
        public Task<IReadOnlyList<EmailMessage>> GetImportantEmailsAsync(DateTimeOffset sinceUtc, CancellationToken ct)
        {
            IReadOnlyList<EmailMessage> list =
            [
                new EmailMessage("Gmail", "msg_123", "Action required: submit report", "Please submit by EOD", DateTimeOffset.UtcNow.AddMinutes(-5), "boss@company.com")
            ];

            return Task.FromResult(list);
        }
    }
}