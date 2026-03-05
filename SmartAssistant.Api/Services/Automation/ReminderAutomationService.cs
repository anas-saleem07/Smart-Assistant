using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SmartAssistant.Api.Data;
using SmartAssistant.Api.Services.AutoReply;
using SmartAssistant.Api.Services.Calendar;
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
        private readonly ICalendarService _calendarService;
        private readonly IAutoReplyService _autoReply;

        public ReminderAutomationService(
            ApplicationDbContext db,
            IReminderService reminderService,
            IEmailClient emailClient,
            ICalendarService calendarService,
            IAutoReplyService autoReply)
        {
            _db = db;
            _reminderService = reminderService;
            _emailClient = emailClient;
            _calendarService = calendarService;
            _autoReply = autoReply;
        }

        public async Task<int> ScanAndCreateRemindersAsync(CancellationToken ct)
        {
            // 1) Load settings
            var settings = await _db.ReminderAutomationSettings
                .FirstAsync(x => x.Id == 1, ct);

            // Mark job as running (monitoring)
            settings.LastRunStatus = "Running";
            settings.LastRunError = null;
            await _db.SaveChangesAsync(ct);

            try
            {
                if (!settings.Enabled)
                {
                    // Record outcome even when disabled
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
                    // Record outcome when no emails found
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

                    // A) First: reminder-level dedupe (most important)
                    if (await _reminderService.ExistsEmailReminderAsync(email.Provider, email.Id))
                    {
                        // Still mark as processed so scan remains fast
                        await MarkProcessedIfNeededAsync(email, ct);
                        continue;
                    }

                    // B) Your existing "EmailProcessed" check can stay (fast skip)
                    var alreadyProcessed = await _db.EmailProcessed
                        .AnyAsync(x => x.Provider == email.Provider && x.MessageId == email.Id, ct);

                    if (alreadyProcessed)
                        continue;

                    // 6) Check if email matches rules
                    var matches = IsMatch(email, keywords);

                    // 7) Always mark as processed (so you do not re-check same email forever)
                    var processedRow = await GetOrCreateProcessedRowAsync(email, ct);

                    if (!matches)
                    {
                        await _db.SaveChangesAsync(ct);
                        continue;
                    }

                    // 8) Decide reminder time
                    var reminderTime = DateTimeOffset.UtcNow.AddMinutes(Math.Max(1, settings.DefaultReminderAfterMinutes));

                    // 9) Build EMAIL reminder
                    var emailReminder = new Reminder
                    {
                        Title = email.Subject,
                        Description = BuildDescription(email),
                        ReminderTime = reminderTime,

                        Type = ReminderType.Email,
                        SourceProvider = email.Provider,
                        SourceId = email.Id
                    };

                    // 10) Save reminder
                    var saved = await _reminderService.AddEmailReminderAsync(emailReminder);

                    // Save reminder + EmailProcessed row first
                    await _db.SaveChangesAsync(ct);

                    // Calendar is mandatory for every NEW reminder.
                    // If Calendar fails, we do NOT fail reminder creation.
                    if (saved != null)
                    {
                        // Prevent duplicates: only create calendar event if not already synced
                        if (string.IsNullOrWhiteSpace(saved.CalendarEventId))
                        {
                            try
                            {
                                var eventId = await _calendarService.CreateEventAsync(saved, settings, ct);

                                saved.CalendarEventId = eventId;
                                saved.CalendarSyncedOn = DateTimeOffset.UtcNow;
                                saved.CalendarSyncError = null;

                                await _db.SaveChangesAsync(ct);
                            }
                            catch (OperationCanceledException)
                            {
                                throw;
                            }
                            catch (Exception ex)
                            {
                                saved.CalendarSyncError = ex.Message;
                                await _db.SaveChangesAsync(ct);
                            }
                        }

                        
                        // Auto reply flow (clean):
                        // 1) Mark reply as needed if email matches reply keywords
                        // 2) Attempt auto-reply now if quota allows
                        if (saved != null && settings.AutoReplyEnabled)
                        {
                            try
                            {
                                // TryAutoReplyAsync should set ReplyNeeded/QueuedOn when needed
                                await _autoReply.TryAutoReplyAsync(email, settings, ct);
                            }
                            catch
                            {
                                // Do not break reminder automation if auto reply fails
                            }
                        }

                        createdCount++;
                    }
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
                // Record failure outcome (keep error short)
                settings.LastRunOn = DateTimeOffset.UtcNow;
                settings.LastRunStatus = "Failed";
                settings.LastRunError = ex.Message;
                await _db.SaveChangesAsync(ct);

                throw;
            }
        }

        private async Task MarkProcessedIfNeededAsync(EmailMessage email, CancellationToken ct)
        {
            // What: Ensure EmailProcessed row exists for this email.
            // Why: Your table has a UNIQUE index on (Provider, MessageId). Two jobs/paths can insert at the same time.
            // This version prevents "Cannot insert duplicate key row" by handling the race condition.

            var existing = await _db.EmailProcessed
                .FirstOrDefaultAsync(x => x.Provider == email.Provider && x.MessageId == email.Id, ct);

            if (existing != null)
                return;

            _db.EmailProcessed.Add(new EmailProcessed
            {
                Provider = email.Provider,
                MessageId = email.Id,
                ProcessedOn = DateTimeOffset.UtcNow
            });

            try
            {
                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                // Another thread/job inserted the same row first.
                // Safe to ignore because we only needed it to exist.
            }
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

        private async Task<EmailProcessed> GetOrCreateProcessedRowAsync(EmailMessage email, CancellationToken ct)
        {
            // What: Get existing EmailProcessed row, or create if missing.
            // Why: Prevent duplicate key crash when same email is scanned twice or jobs run concurrently.

            var existing = await _db.EmailProcessed
                .FirstOrDefaultAsync(x => x.Provider == email.Provider && x.MessageId == email.Id, ct);

            if (existing != null)
                return existing;

            var row = new EmailProcessed
            {
                Provider = email.Provider,
                MessageId = email.Id,
                ProcessedOn = DateTimeOffset.UtcNow
            };

            _db.EmailProcessed.Add(row);

            try
            {
                await _db.SaveChangesAsync(ct);
                return row;
            }
            catch (DbUpdateException)
            {
                // Race condition: another worker inserted the same row first.
                // Re-load it and continue.
                var again = await _db.EmailProcessed
                    .FirstAsync(x => x.Provider == email.Provider && x.MessageId == email.Id, ct);

                return again;
            }
        }
    }
}