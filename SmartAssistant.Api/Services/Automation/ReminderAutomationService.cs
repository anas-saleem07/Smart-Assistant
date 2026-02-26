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

            if (!settings.Enabled)
                return 0;

            // 2) Decide how far back to scan
            //    Keep it simple: last 24 hours
            var sinceUtc = DateTimeOffset.UtcNow.AddHours(-24);

            // 3) Fetch important emails
            var importantEmails = await _emailClient.GetImportantEmailsAsync(sinceUtc, ct);
            if (importantEmails == null || importantEmails.Count == 0)
                return 0;

            // 4) Parse keywords once
            var keywords = ParseKeywords(settings.KeywordsCsv);

            var createdCount = 0;

            foreach (var email in importantEmails)
            {
                // Defensive checks
                if (email == null)
                    continue;

                if (string.IsNullOrWhiteSpace(email.Provider) || string.IsNullOrWhiteSpace(email.Id))
                    continue;

                // 5) Skip if already processed (prevents duplicates)
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
                //    For now: now + DefaultReminderAfterMinutes
                var reminderTime = DateTimeOffset.UtcNow.AddMinutes(Math.Max(1, settings.DefaultReminderAfterMinutes));

                // 9) Build reminder from email
                var reminder = new Reminder
                {
                    Id = Guid.NewGuid(),
                    Title = email.Subject,
                    Description = BuildDescription(email),
                    ReminderTime = reminderTime,
                    Completed = false,
                    CreatedOn = DateTime.UtcNow
                };

                // 10) Save reminder
                await _reminderService.AddReminderAsync(reminder);

                // 11) Save processed marker
                await _db.SaveChangesAsync(ct);

                createdCount++;
            }

            return createdCount;
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