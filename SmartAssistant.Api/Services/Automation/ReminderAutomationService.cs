using Microsoft.EntityFrameworkCore;
using SmartAssistant.Api.Data;
using SmartAssistant.Api.Services.AutoReply;
using SmartAssistant.Api.Services.Calendar;
using SmartAssistant.Api.Services.Email;
using SmartAssistant.Core.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SmartAssistant.Api.Services.Automation
{
    #region Interface
    public interface IReminderAutomationService
    {
        Task<int> ScanAndCreateRemindersAsync(CancellationToken ct);
    }
    #endregion

    public class ReminderAutomationService : IReminderAutomationService
    {
        #region Fields
        private readonly IAutoReplyService _autoReply;
        private readonly ICalendarService _calendarService;
        private readonly ApplicationDbContext _db;
        private readonly IEmailClient _emailClient;
        private readonly IReminderService _reminderService;
        #endregion

        #region Constructor
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
        #endregion

        #region Public methods
        public async Task<int> ScanAndCreateRemindersAsync(CancellationToken ct)
        {
            var settings = await _db.ReminderAutomationSettings
                .FirstAsync(item => item.Id == 1, ct);

            settings.LastRunStatus = "Running";
            settings.LastRunError = null;
            await _db.SaveChangesAsync(ct);

            try
            {
                if (!settings.Enabled)
                {
                    settings.LastRunOn = DateTimeOffset.UtcNow;
                    settings.LastRunCreatedCount = 0;
                    settings.LastRunStatus = "Success";
                    settings.LastRunError = null;
                    await _db.SaveChangesAsync(ct);

                    return 0;
                }

                var activeAccountEmail = await GetActiveAccountEmailAsync(ct);
                var sinceUtc = DateTimeOffset.UtcNow.AddHours(-24);

                var importantEmails = await _emailClient.GetImportantEmailsAsync(sinceUtc, ct, settings.GmailQuery);
                if (importantEmails == null || importantEmails.Count == 0)
                {
                    settings.LastRunOn = DateTimeOffset.UtcNow;
                    settings.LastRunCreatedCount = 0;
                    settings.LastRunStatus = "Success";
                    settings.LastRunError = null;
                    await _db.SaveChangesAsync(ct);

                    return 0;
                }

                var reminderKeywords = ParseKeywords(settings.KeywordsCsv);
                var replyKeywords = ParseKeywords(settings.AutoReplyKeywordsCsv);

                var createdCount = 0;

                foreach (var email in importantEmails)
                {
                    if (email == null)
                        continue;

                    if (string.IsNullOrWhiteSpace(email.Provider) || string.IsNullOrWhiteSpace(email.Id))
                        continue;

                    try
                    {
                        var cancellationHandled = await _autoReply.TryHandleCancellationAsync(email, settings, ct);
                        if (cancellationHandled)
                            continue;
                    }
                    catch
                    {
                    }

                    if (IsCancellationEmail(email))
                    {
                        await MarkProcessedIfNeededAsync(email, activeAccountEmail, ct);
                        continue;
                    }

                    try
                    {
                        var confirmationHandled = await _autoReply.TryCreateConfirmedReminderAsync(email, settings, ct);
                        if (confirmationHandled)
                        {
                            createdCount++;
                            continue;
                        }
                    }
                    catch
                    {
                    }

                    var alreadyProcessed = await _db.EmailProcessed
                        .AnyAsync(item =>
                            item.Provider == email.Provider &&
                            item.MessageId == email.Id &&
                            item.AccountEmail == activeAccountEmail, ct);

                    if (alreadyProcessed)
                        continue;

                    var hasCalendarInvite =
                        email.HasCalendarInvite &&
                        email.InviteStartUtc.HasValue &&
                        email.InviteEndUtc.HasValue;

                    var isReplyCandidate = hasCalendarInvite || IsMatch(email, replyKeywords);

                    var processedRow = await GetOrCreateProcessedRowAsync(email, activeAccountEmail, ct);

                    if (!string.IsNullOrWhiteSpace(email.CalendarEventId) &&
                        string.IsNullOrWhiteSpace(processedRow.CalendarEventId))
                    {
                        processedRow.CalendarEventId = email.CalendarEventId;
                    }

                    if (isReplyCandidate)
                    {
                        await _db.SaveChangesAsync(ct);

                        if (settings.AutoReplyEnabled)
                        {
                            try
                            {
                                await _autoReply.TryAutoReplyAsync(email, settings, ct);
                            }
                            catch
                            {
                            }
                        }

                        continue;
                    }

                    var isReminderCandidate = IsMatch(email, reminderKeywords);
                    if (!isReminderCandidate)
                    {
                        await _db.SaveChangesAsync(ct);
                        continue;
                    }

                    if (await _reminderService.ExistsEmailReminderAsync(email.Provider, email.Id, email.CalendarEventId))
                    {
                        await MarkProcessedIfNeededAsync(email, activeAccountEmail, ct);
                        continue;
                    }

                    var reminderTime = DateTimeOffset.UtcNow.AddMinutes(Math.Max(1, settings.DefaultReminderAfterMinutes));

                    var emailReminder = new Reminder
                    {
                        Title = email.Subject,
                        Description = BuildDescription(email),
                        ReminderTime = reminderTime,
                        Type = ReminderType.Email,
                        SourceProvider = email.Provider,
                        SourceId = email.Id,
                        AccountEmail = activeAccountEmail
                    };

                    var savedReminder = await _reminderService.AddEmailReminderAsync(emailReminder);
                    await _db.SaveChangesAsync(ct);

                    if (savedReminder != null)
                    {
                        createdCount++;
                    }
                }

                settings.LastRunOn = DateTimeOffset.UtcNow;
                settings.LastRunCreatedCount = createdCount;
                settings.LastRunStatus = "Success";
                settings.LastRunError = null;
                await _db.SaveChangesAsync(ct);

                return createdCount;
            }
            catch (Exception ex)
            {
                settings.LastRunOn = DateTimeOffset.UtcNow;
                settings.LastRunStatus = "Failed";
                settings.LastRunError = ex.Message;
                await _db.SaveChangesAsync(ct);

                throw;
            }
        }
        #endregion

        #region Private helpers
        private static string BuildDescription(EmailMessage email)
        {
            var fromPart = string.IsNullOrWhiteSpace(email.From) ? "unknown" : email.From;
            var snippetPart = string.IsNullOrWhiteSpace(email.Snippet) ? "" : email.Snippet;

            return "From: " + fromPart + Environment.NewLine + Environment.NewLine + snippetPart;
        }

        private async Task<string?> GetActiveAccountEmailAsync(CancellationToken ct)
        {
            return await _db.EmailOAuthAccounts
                .Where(accountItem => accountItem.Provider == "Gmail" && accountItem.Active)
                .OrderByDescending(accountItem => accountItem.Id)
                .Select(accountItem => accountItem.Email)
                .FirstOrDefaultAsync(ct);
        }

        private async Task<EmailProcessed> GetOrCreateProcessedRowAsync(
            EmailMessage email,
            string? activeAccountEmail,
            CancellationToken ct)
        {
            var existing = await _db.EmailProcessed
                .FirstOrDefaultAsync(item =>
                    item.Provider == email.Provider &&
                    item.MessageId == email.Id &&
                    item.AccountEmail == activeAccountEmail, ct);

            if (existing != null)
                return existing;

            var row = new EmailProcessed
            {
                Provider = email.Provider,
                MessageId = email.Id,
                AccountEmail = activeAccountEmail,
                ProcessedOn = DateTimeOffset.UtcNow,
                CalendarEventId = email.CalendarEventId
            };

            _db.EmailProcessed.Add(row);

            try
            {
                await _db.SaveChangesAsync(ct);
                return row;
            }
            catch (DbUpdateException)
            {
                var again = await _db.EmailProcessed
                    .FirstAsync(item =>
                        item.Provider == email.Provider &&
                        item.MessageId == email.Id &&
                        item.AccountEmail == activeAccountEmail, ct);

                return again;
            }
        }

        private static bool IsCancellationEmail(EmailMessage email)
        {
            var text = ((email.Subject ?? "") + " " + (email.Snippet ?? "")).ToLowerInvariant();

            if (string.IsNullOrWhiteSpace(text))
                return false;

            return
                text.Contains("cancelled") ||
                text.Contains("canceled") ||
                text.Contains("cancellation") ||
                text.Contains("cancelation") ||
                text.Contains("cancellation notice") ||
                text.Contains("cancelled event") ||
                text.Contains("canceled event") ||
                text.Contains("meeting cancellation") ||
                text.Contains("interview cancellation") ||
                text.Contains("removed from google calendar") ||
                text.Contains("this event has been cancelled") ||
                text.Contains("this event has been canceled");
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

        private async Task MarkProcessedIfNeededAsync(EmailMessage email, string? activeAccountEmail, CancellationToken ct)
        {
            var existing = await _db.EmailProcessed
                .FirstOrDefaultAsync(item =>
                    item.Provider == email.Provider &&
                    item.MessageId == email.Id &&
                    item.AccountEmail == activeAccountEmail, ct);

            if (existing != null)
            {
                if (string.IsNullOrWhiteSpace(existing.ProcessingStatus) && IsCancellationEmail(email))
                {
                    existing.ProcessingStatus = ProcessingStatuses.RejectedBySender;
                    existing.ReplyLastError = existing.ReplyLastError ?? "Cancellation processed.";
                    await _db.SaveChangesAsync(ct);
                }

                return;
            }

            _db.EmailProcessed.Add(new EmailProcessed
            {
                Provider = email.Provider,
                MessageId = email.Id,
                AccountEmail = activeAccountEmail,
                ProcessedOn = DateTimeOffset.UtcNow,
                CalendarEventId = email.CalendarEventId,
                ProcessingStatus = IsCancellationEmail(email) ? ProcessingStatuses.RejectedBySender : null,
                ReplyLastError = IsCancellationEmail(email) ? "Cancellation processed." : null
            });

            try
            {
                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
            }
        }

        private static List<string> ParseKeywords(string csv)
        {
            if (string.IsNullOrWhiteSpace(csv))
                return new List<string>();

            return csv
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(item => item.Trim())
                .Where(item => item.Length > 0)
                .Select(item => item.ToLowerInvariant())
                .ToList();
        }
        #endregion
    }
}