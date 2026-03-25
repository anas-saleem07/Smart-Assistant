using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SmartAssistant.Api.Services.Email
{
    public record EmailMessage(
        string Provider,
        string Id,
        string Subject,
        string Snippet,
        DateTimeOffset ReceivedOn,
        string From,
        bool HasCalendarInvite = false,
        string? CalendarEventId = null,
        DateTimeOffset? InviteStartUtc = null,
        DateTimeOffset? InviteEndUtc = null
    );

    public interface IEmailClient
    {
        Task<IReadOnlyList<EmailMessage>> GetImportantEmailsAsync(
            DateTimeOffset sinceUtc,
            CancellationToken ct,
            string? gmailQuery = null);

        Task<EmailMessage?> GetEmailByIdAsync(string messageId, CancellationToken ct);

        Task ReplyAsync(string messageId, string body, CancellationToken ct);
    }

    public class FakeEmailClient : IEmailClient
    {
        public Task<IReadOnlyList<EmailMessage>> GetImportantEmailsAsync(
            DateTimeOffset sinceUtc,
            CancellationToken ct,
            string? gmailQuery = null)
        {
            IReadOnlyList<EmailMessage> list =
            [
                new EmailMessage(
                    "Gmail",
                    "msg_123",
                    "Interview schedule",
                    "Can you confirm time?",
                    DateTimeOffset.UtcNow.AddMinutes(-5),
                    "boss@company.com",
                    false,
                    null,
                    null,
                    null
                )
            ];

            return Task.FromResult(list);
        }

        public Task ReplyAsync(string messageId, string body, CancellationToken ct)
        {
            return Task.CompletedTask;
        }

        public Task<EmailMessage?> GetEmailByIdAsync(string messageId, CancellationToken ct)
        {
            return Task.FromResult<EmailMessage?>(new EmailMessage(
                "Gmail",
                messageId,
                "Interview schedule",
                "Please confirm your availability.",
                DateTimeOffset.UtcNow,
                "boss@company.com",
                false,
                null,
                null,
                null
            ));
        }
    }
}