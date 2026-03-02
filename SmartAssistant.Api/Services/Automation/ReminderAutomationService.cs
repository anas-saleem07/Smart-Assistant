using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SmartAssistant.Api.Data;
using SmartAssistant.Api.Services.Email;
using SmartAssistant.Core.Entities;

namespace SmartAssistant.Api.Services.Automation
{
    public interface IReminderAutomationService
    {
        Task<int> ScanAndCreateRemindersAsync(CancellationToken ct);
    }

    public class ReminderAutomationService : IReminderAutomationService
    {
        private readonly ApplicationDbContext _db;
        private readonly IReminderService _reminderService;
        private readonly IEmailClient _emailClient;

        public ReminderAutomationService(
            ApplicationDbContext db,
            IReminderService reminderService,
            IEmailClient emailClient)
        {
            _db = db;
            _reminderService = reminderService;
            _emailClient = emailClient;
        }

        public async Task<int> ScanAndCreateRemindersAsync(CancellationToken ct)
        {
            // 1) Load settings
            var settings = await _db.ReminderAutomationSettings
                .FirstAsync(x => x.Id == 1, ct);

            //  Mark job as running (monitoring)
            settings.LastRunStatus = "Running";
            settings.LastRunError = null;
            await _db.SaveChangesAsync(ct);

            try
            {
                if (!settings.Enabled)
                {
                    //  Record outcome even when disabled
                    settings.LastRunOn = DateTimeOffset.UtcNow;
                    settings.LastRunCreatedCount = 0;
                    settings.LastRunStatus = "Success";
                    settings.LastRunError = null;
                    await _db.SaveChangesAsync(ct);

                    return 0;
                }

                // 2) Decide how far back to scan (last 24 hours)
                var sinceUtc = DateTimeOffset.UtcNow.AddHours(-24);

                // 3) Fetch important emails
                var importantEmails = await _emailClient.GetImportantEmailsAsync(sinceUtc, ct, settings.GmailQuery);
                if (importantEmails == null || importantEmails.Count == 0)
                {
                    //  Record outcome when no emails found
                    settings.LastRunOn = DateTimeOffset.UtcNow;
                    settings.LastRunCreatedCount = 0;
                    settings.LastRunStatus = "Success";
                    settings.LastRunError = null;
                    await _db.SaveChangesAsync(ct);

                    return 0;
                }

                // 4) Parse keywords once
                var keywords = ParseKeywords(settings.KeywordsCsv);

                var createdCount = 0;

                foreach (var email in importantEmails)
                {
                    if (email == null)
                        continue;

                    if (string.IsNullOrWhiteSpace(email.Provider) || string.IsNullOrWhiteSpace(email.Id))
                        continue;

                    //  A) First: reminder-level dedupe (most important)
                    if (await _reminderService.ExistsEmailReminderAsync(email.Provider, email.Id))
                    {
                        // Still mark as processed so scan remains fast
                        await MarkProcessedIfNeededAsync(email, ct);
                        continue;
                    }

                    //  B) Your existing "EmailProcessed" check can stay (fast skip)
                    var alreadyProcessed = await _db.EmailProcessed
                        .AnyAsync(x => x.Provider == email.Provider && x.MessageId == email.Id, ct);

                    if (alreadyProcessed)
                        continue;

                    // 6) Check if email matches rules
                    var matches = IsMatch(email, keywords);

                    // 7) Always mark as processed (so you do not re-check same email forever)
                    _db.EmailProcessed.Add(new EmailProcessed
                    {
                        Provider = email.Provider,
                        MessageId = email.Id,
                        ProcessedOn = DateTimeOffset.UtcNow
                    });

                    if (!matches)
                    {
                        await _db.SaveChangesAsync(ct);
                        continue;
                    }

                    // 8) Decide reminder time
                    var reminderTime = DateTimeOffset.UtcNow.AddMinutes(Math.Max(1, settings.DefaultReminderAfterMinutes));

                    //  9) Build EMAIL reminder (separate flow from manual reminders)
                    var emailReminder = new Reminder
                    {
                        Title = email.Subject,
                        Description = BuildDescription(email),
                        ReminderTime = reminderTime,

                        //  MUST set Email type + source
                        Type = ReminderType.Email,
                        SourceProvider = email.Provider,
                        SourceId = email.Id
                    };

                    //  10) Save using EMAIL method (NOT manual method)
                    var saved = await _reminderService.AddEmailReminderAsync(emailReminder);

                    // 11) Save processed marker (already added above)
                    await _db.SaveChangesAsync(ct);

                    if (saved != null)
                        createdCount++;
                }

                // Record success outcome
                settings.LastRunOn = DateTimeOffset.UtcNow;
                settings.LastRunCreatedCount = createdCount;
                settings.LastRunStatus = "Success";
                settings.LastRunError = null;
                await _db.SaveChangesAsync(ct);

                return createdCount;
            }
            catch (Exception ex)
            {
                //  Record failure outcome (keep error short)
                settings.LastRunOn = DateTimeOffset.UtcNow;
                settings.LastRunStatus = "Failed";
                settings.LastRunError = ex.Message;
                await _db.SaveChangesAsync(ct);

                throw; // IMPORTANT: Hangfire must see failure for retries/dashboard
            }
        }

        //  helper to mark processed even on dedupe skip
        private async Task MarkProcessedIfNeededAsync(EmailMessage email, CancellationToken ct)
        {
            var alreadyProcessed = await _db.EmailProcessed
                .AnyAsync(x => x.Provider == email.Provider && x.MessageId == email.Id, ct);

            if (alreadyProcessed)
                return;

            _db.EmailProcessed.Add(new EmailProcessed
            {
                Provider = email.Provider,
                MessageId = email.Id,
                ProcessedOn = DateTimeOffset.UtcNow
            });

            await _db.SaveChangesAsync(ct);
        }

        private static string BuildDescription(EmailMessage email)
        {
            var fromPart = string.IsNullOrWhiteSpace(email.From) ? "unknown" : email.From;
            var snippetPart = string.IsNullOrWhiteSpace(email.Snippet) ? "" : email.Snippet;

            return "From: " + fromPart + Environment.NewLine + Environment.NewLine + snippetPart;
        }

        private static List<string> ParseKeywords(string csv)
        {
            if (string.IsNullOrWhiteSpace(csv))
                return new List<string>();

            return csv
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(k => k.Trim())
                .Where(k => k.Length > 0)
                .Select(k => k.ToLowerInvariant())
                .ToList();
        }

        private static bool IsMatch(EmailMessage email, List<string> keywords)
        {
            // If no keywords configured, do not create reminders automatically
            if (keywords == null || keywords.Count == 0)
                return false;

            var subject = email.Subject ?? "";
            var snippet = email.Snippet ?? "";

            var haystack = (subject + " " + snippet).ToLowerInvariant();

            for (int keywordIndex = 0; keywordIndex < keywords.Count; keywordIndex++)
            {
                var keyword = keywords[keywordIndex];
                if (!string.IsNullOrWhiteSpace(keyword) && haystack.Contains(keyword))
                    return true;
            }

            return false;
        }
    }
}