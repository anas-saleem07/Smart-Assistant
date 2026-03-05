using Hangfire;
using Microsoft.EntityFrameworkCore;
using SmartAssistant.Api.Data;
using SmartAssistant.Api.Services.AutoReply;
using SmartAssistant.Api.Services.Email;

namespace SmartAssistant.Api.Jobs
{
    [DisableConcurrentExecution(600)]
    public sealed class AutoReplyPendingJob
    {
        private readonly ApplicationDbContext _db;
        private readonly IEmailClient _emailClient;
        private readonly IAutoReplyService _autoReply;

        public AutoReplyPendingJob(ApplicationDbContext db, IEmailClient emailClient, IAutoReplyService autoReply)
        {
            _db = db;
            _emailClient = emailClient;
            _autoReply = autoReply;
        }

        public async Task Run(CancellationToken ct)
        {
            var settings = await _db.ReminderAutomationSettings.FirstAsync(x => x.Id == 1, ct);

            if (!settings.AutoReplyEnabled)
                return;

            // Load pending replies (small batch)
            var pending = await _db.EmailProcessed
                .Where(x => x.ReplyNeeded && !x.Replied)
                .OrderBy(x => x.ReplyQueuedOn)
                .Take(10)
                .ToListAsync(ct);

            for (int i = 0; i < pending.Count; i++)
            {
                var row = pending[i];

                // THIS IS THE EXACT BLOCK YOU ASKED ABOUT
                var email = await _emailClient.GetEmailByIdAsync(row.MessageId, ct);
                if (email == null)
                {
                    row.ReplyLastError = "Could not load email from Gmail.";
                    await _db.SaveChangesAsync(ct);
                    continue;
                }

                // Now we can attempt auto reply using the same service
                // AutoReplyService will handle quota + keyword match + replying
                await _autoReply.TryAutoReplyAsync(email, settings, ct);
            }
        }
    }
}