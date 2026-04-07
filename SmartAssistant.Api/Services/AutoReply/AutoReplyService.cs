using Microsoft.EntityFrameworkCore;
using SmartAssistant.Api.Data;
using SmartAssistant.Api.Helpers;
using SmartAssistant.Api.Services.Ai;
using SmartAssistant.Api.Services.Calendar;
using SmartAssistant.Api.Services.Email;
using SmartAssistant.Core.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Globalization;

namespace SmartAssistant.Api.Services.AutoReply
{
    #region Interface and DTOs
    public interface IAutoReplyService
    {
        Task<bool> TryAutoReplyAsync(EmailMessage email, ReminderAutomationSettings settings, CancellationToken ct);
        Task<bool> TryHandleCancellationAsync(EmailMessage email, ReminderAutomationSettings settings, CancellationToken ct);
        Task<bool> TryCreateConfirmedReminderAsync(EmailMessage email, ReminderAutomationSettings settings, CancellationToken ct);

        Task<bool> SendApprovedReplyAsync(long emailProcessedId, CancellationToken ct);

        Task<List<PendingAutoReplyDto>> GetPendingApprovalsAsync(CancellationToken ct);
        Task<bool> ApprovePendingReplyAsync(long emailProcessedId, bool useSuggestedSlot, CancellationToken ct);
        Task<bool> RejectPendingReplyAsync(long emailProcessedId, CancellationToken ct);
        Task<ApprovalCalendarOpenDto> CreateSuggestedCalendarEventAsync(long emailProcessedId, CancellationToken ct);
        Task<List<ProcessedEmailHistoryDto>> GetProcessedEmailHistoryAsync(CancellationToken ct);
    }

    public sealed class PendingAutoReplyDto
    {
        public long Id { get; set; }
        public string Provider { get; set; } = "";
        public string MessageId { get; set; } = "";
        public string Subject { get; set; } = "";
        public string From { get; set; } = "";
        public string RequestSummary { get; set; } = "";
        public DateTimeOffset? ProposedStartUtc { get; set; }
        public DateTimeOffset? ProposedEndUtc { get; set; }
        public string ProposedLocalText { get; set; } = "";
        public string SuggestedLocalText { get; set; } = "";
        public DateTimeOffset? SuggestedStartUtc { get; set; }
        public string? ReplyLastError { get; set; }
    }

    public sealed class ApprovalCalendarOpenDto
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public string EventHtmlLink { get; set; } = "";
        public string EventId { get; set; } = "";
        public DateTimeOffset? StartUtc { get; set; }
        public DateTimeOffset? EndUtc { get; set; }
    }
    public sealed class ProcessedEmailHistoryDto
    {
        public long Id { get; set; }
        public string Provider { get; set; } = "";
        public string MessageId { get; set; } = "";
        public string Subject { get; set; } = "";
        public string From { get; set; } = "";
        public string ProcessingStatus { get; set; } = "";
        public string Details { get; set; } = "";
        public DateTimeOffset? ProposedStartUtc { get; set; }
        public DateTimeOffset? ProposedEndUtc { get; set; }
        public DateTimeOffset? SuggestedStartUtc { get; set; }
        public DateTimeOffset? SuggestedEndUtc { get; set; }
        public DateTimeOffset ProcessedOn { get; set; }
        public DateTimeOffset? RepliedOn { get; set; }
    }
    #endregion

    public sealed class AutoReplyService : IAutoReplyService
    {
        private readonly ApplicationDbContext _db;
        private readonly IAiClient _ai;
        private readonly IEmailClient _emailClient;
        private readonly ICalendarService _calendar;

        #region Constructor
        public AutoReplyService(ApplicationDbContext db, IAiClient ai, IEmailClient emailClient, ICalendarService calendar)
        {
            _db = db;
            _ai = ai;
            _emailClient = emailClient;
            _calendar = calendar;
        }
        #endregion

        #region Public service methods
        public async Task<bool> TryHandleCancellationAsync(EmailMessage email, ReminderAutomationSettings settings, CancellationToken ct)
        {
            if (!IsCancellationRequest(email))
                return false;

            var normalizedCancelledSubject = NormalizeCancellationSubject(email.Subject);

            DateTimeOffset? cancelledStartUtc = null;
            DateTimeOffset? cancelledEndUtc = null;

            if (email.HasCalendarInvite && email.InviteStartUtc.HasValue && email.InviteEndUtc.HasValue)
            {
                cancelledStartUtc = email.InviteStartUtc.Value.ToUniversalTime();
                cancelledEndUtc = email.InviteEndUtc.Value.ToUniversalTime();
            }
            else if (TryGetProposedUtcRange(email, settings, out var parsedStartUtc, out var parsedEndUtc))
            {
                cancelledStartUtc = parsedStartUtc;
                cancelledEndUtc = parsedEndUtc;
            }

            var recentCutoffUtc = DateTimeOffset.UtcNow.AddDays(-14);

            var recentProcessedRows = await _db.EmailProcessed
                .Where(item =>
                    item.Provider == email.Provider &&
                    item.ProcessedOn >= recentCutoffUtc)
                .OrderByDescending(item => item.ProcessedOn)
                .ToListAsync(ct);

            var relatedProcessedRows = recentProcessedRows
                .Where(item =>
                    (item.Provider == email.Provider && item.MessageId == email.Id) ||
                    (!string.IsNullOrWhiteSpace(email.CalendarEventId) &&
                        (
                            item.CalendarEventId == email.CalendarEventId ||
                            item.SuggestedCalendarEventId == email.CalendarEventId
                        )) ||
                    (cancelledStartUtc.HasValue &&
                        (
                            (item.ProposedStartUtc.HasValue && item.ProposedStartUtc.Value == cancelledStartUtc.Value) ||
                            (item.SuggestedStartUtc.HasValue && item.SuggestedStartUtc.Value == cancelledStartUtc.Value)
                        )))
                .ToList();

            var recentReminders = await _db.Reminder
                .Where(item => item.CreatedOn >= recentCutoffUtc.UtcDateTime)
                .ToListAsync(ct);

            var relatedReminders = recentReminders
                .Where(item =>
                    (!string.IsNullOrWhiteSpace(email.CalendarEventId) &&
                        item.CalendarEventId == email.CalendarEventId) ||
                    (relatedProcessedRows.Any(processedRow =>
                        !string.IsNullOrWhiteSpace(item.SourceProvider) &&
                        !string.IsNullOrWhiteSpace(item.SourceId) &&
                        item.SourceProvider == processedRow.Provider &&
                        item.SourceId == processedRow.MessageId)) ||
                    (!string.IsNullOrWhiteSpace(normalizedCancelledSubject) &&
                        !string.IsNullOrWhiteSpace(item.Title) &&
                        NormalizeCancellationSubject(item.Title) == normalizedCancelledSubject) ||
                    (cancelledStartUtc.HasValue && item.ReminderTime == cancelledStartUtc.Value) ||
                    (cancelledStartUtc.HasValue &&
                        relatedProcessedRows.Any(processedRow =>
                            (processedRow.ProposedStartUtc.HasValue && item.ReminderTime == processedRow.ProposedStartUtc.Value) ||
                            (processedRow.SuggestedStartUtc.HasValue && item.ReminderTime == processedRow.SuggestedStartUtc.Value))))
                .ToList();

            if (!string.IsNullOrWhiteSpace(email.CalendarEventId))
            {
                await _calendar.DeleteEventAsync(email.CalendarEventId, settings, ct);
            }

            foreach (var processedRow in relatedProcessedRows)
            {
                if (!string.IsNullOrWhiteSpace(processedRow.CalendarEventId))
                {
                    await _calendar.DeleteEventAsync(processedRow.CalendarEventId, settings, ct);
                }

                if (!string.IsNullOrWhiteSpace(processedRow.SuggestedCalendarEventId))
                {
                    await _calendar.DeleteEventAsync(processedRow.SuggestedCalendarEventId, settings, ct);
                }
            }

            foreach (var reminder in relatedReminders)
            {
                if (!string.IsNullOrWhiteSpace(reminder.CalendarEventId))
                {
                    await _calendar.DeleteEventAsync(reminder.CalendarEventId, settings, ct);
                }
            }

            if (relatedReminders.Count > 0)
            {
                _db.Reminder.RemoveRange(relatedReminders);
            }

            foreach (var row in relatedProcessedRows)
            {
                row.ReplyNeeded = false;
                row.ReplyRequiresApproval = false;
                row.ReplyDraftBody = null;
                row.WaitingForExternalConfirmation = false;
                row.ReplyLastError = "Cancellation processed. Related reminder and calendar event removed.";
                row.SuggestedCalendarHtmlLink = null;
                row.SuggestedCalendarEventId = null;
                row.SuggestedStartUtc = null;
                row.SuggestedEndUtc = null;
                row.Replied = false;
                row.RepliedOn = null;

                SetProcessingStatus(row, ProcessingStatuses.RejectedBySender);
            }

            var existingCancellationRow = await _db.EmailProcessed
                .FirstOrDefaultAsync(item => item.Provider == email.Provider && item.MessageId == email.Id, ct);

            if (existingCancellationRow == null)
            {
                _db.EmailProcessed.Add(new EmailProcessed
                {
                    Provider = email.Provider,
                    MessageId = email.Id,
                    ProcessedOn = DateTimeOffset.UtcNow,
                    CalendarEventId = email.CalendarEventId,
                    ReplyNeeded = false,
                    Replied = false,
                    WaitingForExternalConfirmation = false,
                    ReplyLastError = "Cancellation processed.",
                    ProcessingStatus = ProcessingStatuses.RejectedBySender
                });
            }
            else
            {
                existingCancellationRow.ReplyLastError = "Cancellation processed.";
                SetProcessingStatus(existingCancellationRow, ProcessingStatuses.RejectedBySender);
            }

            await _db.SaveChangesAsync(ct);
            return true;
        }

        public async Task<bool> TryCreateConfirmedReminderAsync(EmailMessage email, ReminderAutomationSettings settings, CancellationToken ct)
        {
            if (IsCancellationRequest(email))
                return false;

            if (!IsFinalConfirmationEmail(email))
                return false;

            var currentEmailProcessed = await GetOrCreateEmailProcessedRowAsync(email, ct);
            var cutoffUtc = DateTimeOffset.UtcNow.AddDays(-3);

            var candidateRows = await _db.EmailProcessed
                .Where(item =>
                    item.Provider == email.Provider &&
                    item.Replied &&
                    !item.ReplyNeeded &&
                    item.SuggestedStartUtc.HasValue &&
                    item.WaitingForExternalConfirmation &&
                    item.RepliedOn.HasValue &&
                    item.RepliedOn.Value >= cutoffUtc)
                .OrderByDescending(item => item.RepliedOn ?? item.ProcessedOn)
                .Take(20)
                .ToListAsync(ct);

            EmailProcessed? matchedRow = null;

            for (int candidateIndex = 0; candidateIndex < candidateRows.Count; candidateIndex++)
            {
                var candidateRow = candidateRows[candidateIndex];

                var reminderAlreadyExists = await _db.Reminder.AnyAsync(item =>
                    item.SourceProvider == candidateRow.Provider &&
                    item.SourceId == candidateRow.MessageId, ct);

                if (reminderAlreadyExists)
                    continue;

                if (!IsMatchingConfirmationForPendingRow(email, candidateRow))
                    continue;

                matchedRow = candidateRow;
                break;
            }

            if (matchedRow == null)
            {
                currentEmailProcessed.ReplyLastError = "Confirmation email received, but no matching reschedule request was found.";
                SetProcessingStatus(currentEmailProcessed, ProcessingStatuses.Error);
                await _db.SaveChangesAsync(ct);
                return false;
            }

            var finalStartUtc = matchedRow.SuggestedStartUtc;
            if (!finalStartUtc.HasValue)
            {
                currentEmailProcessed.ReplyLastError = "Confirmation email received, but no final rescheduled meeting time was available.";
                SetProcessingStatus(currentEmailProcessed, ProcessingStatuses.Error);
                await _db.SaveChangesAsync(ct);
                return false;
            }

            var reminderTitle = string.IsNullOrWhiteSpace(email.Subject)
                ? "Confirmed meeting"
                : email.Subject.Trim();

            var latestReplyText = GetLatestReplyText(email);

            var reminderDescription =
                "Confirmed from email reply." + Environment.NewLine + Environment.NewLine +
                "Original scheduling message id: " + matchedRow.MessageId;

            if (!string.IsNullOrWhiteSpace(email.From))
            {
                reminderDescription += Environment.NewLine + "From: " + email.From;
            }

            if (!string.IsNullOrWhiteSpace(latestReplyText))
            {
                reminderDescription += Environment.NewLine + Environment.NewLine + latestReplyText;
            }

            var reminder = new Reminder
            {
                Title = reminderTitle,
                Description = reminderDescription,
                ReminderTime = finalStartUtc.Value,
                Type = ReminderType.Email,
                SourceProvider = matchedRow.Provider,
                SourceId = matchedRow.MessageId
            };

            var savedReminder = await _db.Reminder
                .FirstOrDefaultAsync(item =>
                    item.SourceProvider == matchedRow.Provider &&
                    item.SourceId == matchedRow.MessageId, ct);

            if (savedReminder == null)
            {
                savedReminder = await AddConfirmedReminderAsync(reminder, ct);
            }

            if (savedReminder != null)
            {
                await EnsureCalendarEventForReminderAsync(savedReminder, settings, ct);
            }

            if (!string.IsNullOrWhiteSpace(matchedRow.SuggestedCalendarEventId))
            {
                try
                {
                    await _calendar.DeleteEventAsync(matchedRow.SuggestedCalendarEventId, settings, ct);
                }
                catch
                {
                }
            }

            matchedRow.WaitingForExternalConfirmation = false;
            matchedRow.ReplyRequiresApproval = false;
            matchedRow.ReplyDraftBody = null;
            matchedRow.ReplyLastError = "Confirmed by sender. Reminder created.";
            matchedRow.SuggestedCalendarHtmlLink = null;
            matchedRow.SuggestedCalendarEventId = null;
            matchedRow.SuggestedStartUtc = null;
            matchedRow.SuggestedEndUtc = null;
            SetProcessingStatus(matchedRow, ProcessingStatuses.Confirmed);

            currentEmailProcessed.ReplyLastError = "Confirmation processed. Reminder created.";
            SetProcessingStatus(currentEmailProcessed, ProcessingStatuses.Confirmed);

            await _db.SaveChangesAsync(ct);
            return true;
        }

        public async Task<bool> TryAutoReplyAsync(EmailMessage email, ReminderAutomationSettings settings, CancellationToken ct)
        {
            if (!settings.AutoReplyEnabled)
                return false;

            if (IsCancellationRequest(email))
                return false;

            // Final confirmation reply should not be treated as a new scheduling email.
            if (IsFinalConfirmationEmail(email))
                return false;

            var replyKeywords = ParseKeywords(settings.AutoReplyKeywordsCsv);

            var hasStructuredInvite =
                email.HasCalendarInvite &&
                email.InviteStartUtc.HasValue &&
                email.InviteEndUtc.HasValue;

            var isRescheduleRequest = IsRescheduleRequest(email);

            var looksLikeSchedulingEmail =
                hasStructuredInvite ||
                isRescheduleRequest ||
                IsMatch(email, replyKeywords) ||
                LooksLikeInviteWithTime(email);

            if (!looksLikeSchedulingEmail)
                return false;

            var processed = await GetOrCreateEmailProcessedRowAsync(email, ct);
            var processedChanged = false;

            if (string.IsNullOrWhiteSpace(processed.Subject) && !string.IsNullOrWhiteSpace(email.Subject))
            {
                processed.Subject = email.Subject.Trim();
                processedChanged = true;
            }

            if (string.IsNullOrWhiteSpace(processed.From) && !string.IsNullOrWhiteSpace(email.From))
            {
                processed.From = email.From.Trim();
                processedChanged = true;
            }

            if (processedChanged)
            {
                await _db.SaveChangesAsync(ct);
            }          

            if (processed.Replied)
                return true;

            if (!processed.ReplyNeeded)
            {
                processed.ReplyNeeded = true;
                processed.ReplyQueuedOn = DateTimeOffset.UtcNow;
                processed.ReplyLastError = null;
                await _db.SaveChangesAsync(ct);
            }

            var todayUtc = DateTime.UtcNow.Date;
            if (!settings.AiUsageDayUtc.HasValue || settings.AiUsageDayUtc.Value.Date != todayUtc)
            {
                settings.AiUsageDayUtc = todayUtc;
                settings.AiCallsToday = 0;
                settings.AiPausedUntilUtc = null;
                settings.AiLastError = null;
                await _db.SaveChangesAsync(ct);
            }

            if (settings.AiPausedUntilUtc.HasValue && settings.AiPausedUntilUtc.Value > DateTimeOffset.UtcNow)
            {
                processed.ReplyLastError = "AI paused until " + settings.AiPausedUntilUtc.Value.ToString("u");
                SetProcessingStatus(processed, ProcessingStatuses.Error);
                await _db.SaveChangesAsync(ct);
                return false;
            }

            if (settings.AiCallsToday >= Math.Max(1, settings.AiDailyLimit))
            {
                settings.AiPausedUntilUtc = DateTimeOffset.UtcNow.Date.AddDays(1);
                settings.AiLastError = "AI quota reached.";
                await _db.SaveChangesAsync(ct);

                processed.ReplyLastError = "AI quota reached. Will retry later.";
                SetProcessingStatus(processed, ProcessingStatuses.Error);
                await _db.SaveChangesAsync(ct);
                return false;
            }

            var account = await _db.EmailOAuthAccounts
                .Where(item => item.Provider == email.Provider && item.Active)
                .OrderByDescending(item => item.Id)
                .FirstOrDefaultAsync(ct);

            var senderName =
                !string.IsNullOrWhiteSpace(account?.DisplayName)
                    ? account.DisplayName.Trim()
                    : (!string.IsNullOrWhiteSpace(account?.Email) ? account.Email.Trim() : "SmartAssistant User");

            var greeting = DetectReplyGreeting(email);

            if (TryGetProposedUtcRange(email, settings, out var proposedStartUtc, out var proposedEndUtc))
            {
                processed.ProposedStartUtc = proposedStartUtc;
                processed.ProposedEndUtc = proposedEndUtc;

                if (!string.IsNullOrWhiteSpace(email.CalendarEventId))
                    processed.CalendarEventId = email.CalendarEventId;

                await _db.SaveChangesAsync(ct);

                if (IsOutsideOfficeHours(proposedStartUtc, proposedEndUtc, settings))
                {
                    if (!settings.AllowAutoReplyAfterOfficeHours)
                    {
                        var draft =
                            greeting + "\n\n" +
                            "Thank you for reaching out. The proposed time is outside my regular office hours.\n" +
                            "If you would like, I can confirm this slot, or we can reschedule within office hours.\n" +
                            "Please let me know your preference.\n\n" +
                            "Best regards,\n" +
                            senderName;

                        processed.ReplyRequiresApproval = true;
                        processed.ReplyDraftBody = draft;
                        processed.ReplyLastError = "Approval required: proposed time is outside office hours.";
                        processed.WaitingForExternalConfirmation = false;
                        SetProcessingStatus(processed, ProcessingStatuses.ApprovalPending);
                        await _db.SaveChangesAsync(ct);
                        return false;
                    }
                }
                //var localProposedStart = AppTimeHelper.FormatUtcAsLocal(proposedStartUtc, settings.TimezoneId, "dddd, MMM d, yyyy h:mm tt");
                //var localProposedEnd = AppTimeHelper.FormatUtcAsLocal(proposedEndUtc, settings.TimezoneId, "dddd, MMM d, yyyy h:mm tt");

                //processed.ReplyLastError =
                //    "DEBUG Proposed UTC: " + proposedStartUtc.ToString("u") +
                //    " | Proposed Local: " + localProposedStart +
                //    " | End Local: " + localProposedEnd;

                //await _db.SaveChangesAsync(ct);
                bool isFree;
                try
                {
                    var hasReminderConflict = await HasReminderConflictAsync(proposedStartUtc, proposedEndUtc, ct);
                    var calendarFree = await _calendar.IsFreeAsync(proposedStartUtc, proposedEndUtc, settings, ct);

                    isFree = calendarFree && !hasReminderConflict;
                }
                catch (Exception ex)
                {
                    processed.ReplyLastError = "Calendar check failed: " + ex.Message;
                    SetProcessingStatus(processed, ProcessingStatuses.Error);
                    await _db.SaveChangesAsync(ct);
                    return false;
                }

                if (isFree)
                {
                    // Required flow:
                    // reminder now -> acceptance mail -> calendar event add

                    var savedReminder = await CreateReminderForAcceptedSlotAsync(
                        processed,
                        email.Subject ?? "Confirmed meeting",
                        GetLatestReplyText(email),
                        email.From ?? "",
                        ct);

                    if (!string.IsNullOrWhiteSpace(email.CalendarEventId))
                    {
                        var accepted = await _calendar.AcceptInviteAsync(email.CalendarEventId, settings, ct);
                        if (!accepted)
                        {
                            processed.ReplyLastError = "Calendar invite acceptance failed.";
                            SetProcessingStatus(processed, ProcessingStatuses.Error);
                            await _db.SaveChangesAsync(ct);
                            return false;
                        }
                    }

                    var reply =
                        greeting + "\n\n" +
                        "Thank you for reaching out. Yes, I am available at the proposed time.\n" +
                        "Please proceed accordingly.\n\n" +
                        "Best regards,\n" +
                        senderName;

                    await _emailClient.ReplyAsync(email.Id, reply, ct);

                    if (savedReminder != null)
                    {
                        await EnsureCalendarEventForReminderAsync(savedReminder, settings, ct);
                    }

                    processed.WaitingForExternalConfirmation = false;
                    processed.Replied = true;
                    processed.RepliedOn = DateTimeOffset.UtcNow;
                    processed.ReplyNeeded = false;
                    processed.ReplyLastError = null;
                    SetProcessingStatus(processed, ProcessingStatuses.AutoAccepted);
                    settings.AiCallsToday += 1;
                    await _db.SaveChangesAsync(ct);

                    return true;
                }

                DateTimeOffset? nextFreeUtc = null;
                try
                {
                    nextFreeUtc = await _calendar.FindNextFreeSlotOnSameDayAsync(proposedStartUtc, settings, ct);

                    if (!nextFreeUtc.HasValue)
                        nextFreeUtc = await _calendar.FindNextFreeSlotAsync(proposedStartUtc, settings, ct);

                    if (nextFreeUtc.HasValue)
                        nextFreeUtc = await MoveToNextReminderSafeSlotAsync(nextFreeUtc.Value, settings, ct);
                }
                catch
                {
                }

                var rescheduleLine =
                    nextFreeUtc.HasValue
                        ? "I am not available at the proposed time. Could we reschedule to " + FormatLocal(nextFreeUtc.Value, settings) + "?"
                        : "I am not available at the proposed time. Could you please share another suitable time within office hours?";

                var replyBody =
                    greeting + "\n\n" +
                    "Thank you for reaching out.\n" +
                    rescheduleLine + "\n\n" +
                    "Best regards,\n" +
                    senderName;

                processed.ReplyNeeded = true;
                processed.ReplyRequiresApproval = true;
                processed.ReplyDraftBody = replyBody;
                processed.ReplyLastError = "Approval required: proposed time is busy. Review suggested reschedule before sending.";
                processed.SuggestedStartUtc = nextFreeUtc;
                processed.SuggestedEndUtc = nextFreeUtc.HasValue
                    ? nextFreeUtc.Value.AddMinutes(settings.SlotMinutes)
                    : null;
                processed.WaitingForExternalConfirmation = false;
                SetProcessingStatus(processed, ProcessingStatuses.ApprovalPending);
                await _db.SaveChangesAsync(ct);
                return false;
            }

            if (looksLikeSchedulingEmail)
            {
                processed.ReplyNeeded = true;
                processed.ReplyRequiresApproval = true;
                processed.ReplyDraftBody = null;
                processed.ReplyLastError = "Approval required: scheduling email detected but exact proposed time could not be parsed.";
                SetProcessingStatus(processed, ProcessingStatuses.ApprovalPending);
                await _db.SaveChangesAsync(ct);

                return false;
            }

            var prompt =
                "Write a short professional email reply.\n" +
                "Rules:\n" +
                "- The email is about scheduling but does not contain a specific time.\n" +
                "- Say you are available during office hours and ask them to confirm a suitable time.\n" +
                "- Do not list multiple time slots.\n" +
                "- Keep it concise.\n" +
                "- Do NOT include greeting or closing.\n" +
                "- Do NOT include placeholders.\n\n" +
                "Email subject: " + (email.Subject ?? "") + "\n" +
                "Email snippet: " + (email.Snippet ?? "") + "\n\n" +
                "Reply body only.";

            string aiReplyBody;
            try
            {
                aiReplyBody = await _ai.GenerateAsync(prompt, ct);
            }
            catch (Exception ex)
            {
                processed.ReplyLastError = "AI error: " + ex.Message;
                SetProcessingStatus(processed, ProcessingStatuses.Error);
                await _db.SaveChangesAsync(ct);
                return false;
            }

            if (string.IsNullOrWhiteSpace(aiReplyBody))
            {
                processed.ReplyLastError = "AI returned empty reply.";
                SetProcessingStatus(processed, ProcessingStatuses.Error);
                await _db.SaveChangesAsync(ct);
                return false;
            }

            aiReplyBody =
                greeting + "\n\n" +
                aiReplyBody.Trim() +
                "\n\nBest regards,\n" +
                senderName;

            aiReplyBody = EnsureGreetingFirstLine(aiReplyBody, greeting);

            await _emailClient.ReplyAsync(email.Id, aiReplyBody.Trim(), ct);

            processed.Replied = true;
            processed.RepliedOn = DateTimeOffset.UtcNow;
            processed.ReplyNeeded = false;
            processed.ReplyLastError = null;
            SetProcessingStatus(processed, ProcessingStatuses.AutoAccepted);
            settings.AiCallsToday += 1;
            await _db.SaveChangesAsync(ct);

            return true;
        }

        public async Task<bool> SendApprovedReplyAsync(long emailProcessedId, CancellationToken ct)
        {
            var row = await _db.EmailProcessed.FirstOrDefaultAsync(item => item.Id == emailProcessedId, ct);
            if (row == null)
                return false;

            if (row.Replied)
                return true;

            if (!row.ReplyRequiresApproval)
                return false;

            if (string.IsNullOrWhiteSpace(row.ReplyDraftBody))
                return false;

            // Keep backward compatibility if any old controller still calls this method.
            // Route to the correct approval flow based on the draft type.
            var useSuggestedSlot = IsSuggestedSlotDraft(row.ReplyDraftBody);
            return await ApprovePendingReplyAsync(emailProcessedId, useSuggestedSlot, ct);
        }

        public async Task<ApprovalCalendarOpenDto> CreateSuggestedCalendarEventAsync(long emailProcessedId, CancellationToken ct)
        {
            var result = new ApprovalCalendarOpenDto
            {
                Success = false,
                Message = "Unable to create draft calendar event."
            };

            var row = await _db.EmailProcessed.FirstOrDefaultAsync(item => item.Id == emailProcessedId, ct);
            if (row == null)
            {
                result.Message = "Pending approval record was not found.";
                return result;
            }

            if (!row.ReplyNeeded || row.Replied || !row.ReplyRequiresApproval)
            {
                result.Message = "This request is no longer pending approval.";
                return result;
            }

            var settings = await _db.ReminderAutomationSettings.FirstAsync(item => item.Id == 1, ct);

            var searchFromUtc = row.ProposedStartUtc ?? DateTimeOffset.UtcNow;

            DateTimeOffset? suggestedStartUtc = await _calendar.FindNextFreeSlotOnSameDayAsync(
                searchFromUtc,
                settings,
                ct);

            if (!suggestedStartUtc.HasValue)
            {
                suggestedStartUtc = await _calendar.FindNextFreeSlotAsync(
                    searchFromUtc,
                    settings,
                    ct);
            }

            if (suggestedStartUtc.HasValue)
            {
                suggestedStartUtc = await MoveToNextReminderSafeSlotAsync(
                    suggestedStartUtc.Value,
                    settings,
                    ct);
            }

            if (!suggestedStartUtc.HasValue)
            {
                result.Message = "No suggested slot could be found.";
                return result;
            }

            var slotDurationMinutes = settings.SlotMinutes;

            if (row.ProposedStartUtc.HasValue && row.ProposedEndUtc.HasValue)
            {
                var proposedDuration = row.ProposedEndUtc.Value - row.ProposedStartUtc.Value;
                if (proposedDuration.TotalMinutes > 0)
                {
                    slotDurationMinutes = (int)proposedDuration.TotalMinutes;
                }
            }

            var suggestedEndUtc = suggestedStartUtc.Value.AddMinutes(slotDurationMinutes);

            if (!string.IsNullOrWhiteSpace(row.SuggestedCalendarEventId))
            {
                try
                {
                    await _calendar.DeleteEventAsync(row.SuggestedCalendarEventId, settings, ct);
                }
                catch
                {
                    // Ignore cleanup failure for old draft event.
                }
            }

            var draftEventTitle = "Tentative: Suggested meeting slot";
            var draftEventDescription =
                "This is a draft suggested slot created by SmartAssistant." + Environment.NewLine +
                "It is not confirmed yet." + Environment.NewLine +
                "Final confirmation will happen only after the other side confirms by email.";

            var calendarCreateResult = await _calendar.CreateApprovalSuggestionEventAsync(
                suggestedStartUtc.Value,
                suggestedEndUtc,
                draftEventTitle,
                draftEventDescription,
                settings,
                ct);

            if (calendarCreateResult == null)
            {
                result.Message = "Google Calendar draft event could not be created.";
                return result;
            }

            row.SuggestedStartUtc = suggestedStartUtc.Value;
            row.SuggestedEndUtc = suggestedEndUtc;
            row.SuggestedCalendarEventId = calendarCreateResult.EventId;
            row.SuggestedCalendarHtmlLink = calendarCreateResult.EventHtmlLink;
            row.CalendarCreatedOn = DateTimeOffset.UtcNow;
            row.CalendarLastError = null;
            var suggestedRangeText = FormatLocalRange(
    suggestedStartUtc.Value,
    suggestedEndUtc,
    settings);
            row.ReplyLastError = "Draft suggested slot saved successfully for " + suggestedRangeText + ".";

            await _db.SaveChangesAsync(ct);

            result.Success = true;
            result.Message = "Draft suggested slot saved successfully for " + suggestedRangeText + ".";
            result.EventHtmlLink = calendarCreateResult.EventHtmlLink;
            result.EventId = calendarCreateResult.EventId;
            result.StartUtc = suggestedStartUtc.Value;
            result.EndUtc = suggestedEndUtc;

            return result;
        }

        public async Task<List<PendingAutoReplyDto>> GetPendingApprovalsAsync(CancellationToken ct)
        {
            var settings = await _db.ReminderAutomationSettings.FirstAsync(item => item.Id == 1, ct);

            var pendingRows = await _db.EmailProcessed
                .Where(item =>
                    item.ReplyNeeded &&
                    !item.Replied &&
                    item.ReplyRequiresApproval)
                .OrderByDescending(item => item.ReplyQueuedOn)
                .ToListAsync(ct);

            var result = new List<PendingAutoReplyDto>();

            foreach (var pendingRow in pendingRows)
            {
                DateTimeOffset? suggestedStartUtc = null;
                string suggestedLocalText = "";

                try
                {
                    if (!string.IsNullOrWhiteSpace(pendingRow.SuggestedCalendarEventId))
                    {
                        var latestEvent = await _calendar.GetEventSnapshotAsync(
                            pendingRow.SuggestedCalendarEventId,
                            settings,
                            ct);

                        if (latestEvent != null && latestEvent.StartUtc.HasValue)
                        {
                            pendingRow.SuggestedStartUtc = latestEvent.StartUtc;
                            pendingRow.SuggestedEndUtc = latestEvent.EndUtc;
                            pendingRow.SuggestedCalendarHtmlLink = latestEvent.HtmlLink;
                            await _db.SaveChangesAsync(ct);
                        }
                    }

                    if (pendingRow.SuggestedStartUtc.HasValue)
                    {
                        suggestedStartUtc = pendingRow.SuggestedStartUtc.Value;
                    }
                    else
                    {
                        var searchFromUtc = pendingRow.ProposedStartUtc ?? DateTimeOffset.UtcNow;

                        suggestedStartUtc = await _calendar.FindNextFreeSlotOnSameDayAsync(
                            searchFromUtc,
                            settings,
                            ct);

                        if (!suggestedStartUtc.HasValue)
                        {
                            suggestedStartUtc = await _calendar.FindNextFreeSlotAsync(
                                searchFromUtc,
                                settings,
                                ct);
                        }

                        if (suggestedStartUtc.HasValue)
                        {
                            suggestedStartUtc = await MoveToNextReminderSafeSlotAsync(
                                suggestedStartUtc.Value,
                                settings,
                                ct);
                        }
                    }

                    if (suggestedStartUtc.HasValue)
                    {
                        var suggestedEndUtc = pendingRow.SuggestedEndUtc;

                        if (!suggestedEndUtc.HasValue && suggestedStartUtc.HasValue)
                        {
                            suggestedEndUtc = suggestedStartUtc.Value.AddMinutes(settings.SlotMinutes);
                        }

                        if (suggestedStartUtc.HasValue && suggestedEndUtc.HasValue)
                        {
                            suggestedLocalText = FormatLocalRange(suggestedStartUtc.Value, suggestedEndUtc.Value, settings);
                        }
                        else if (suggestedStartUtc.HasValue)
                        {
                            suggestedLocalText = FormatLocal(suggestedStartUtc.Value, settings);
                        }
                    }
                }
                catch
                {
                    suggestedStartUtc = null;
                    suggestedLocalText = "";
                }

                result.Add(new PendingAutoReplyDto
                {
                    Id = pendingRow.Id,
                    Provider = pendingRow.Provider ?? "",
                    MessageId = pendingRow.MessageId ?? "",
                    Subject = !string.IsNullOrWhiteSpace(pendingRow.Subject)
              ? pendingRow.Subject
              : "Pending scheduling approval",
                    From = pendingRow.From ?? "",
                    RequestSummary = pendingRow.ReplyLastError ?? "Approval required.",
                    ProposedStartUtc = pendingRow.ProposedStartUtc,
                    ProposedEndUtc = pendingRow.ProposedEndUtc,
                    ProposedLocalText = pendingRow.ProposedStartUtc.HasValue
              ? FormatLocal(pendingRow.ProposedStartUtc.Value, settings)
              : "",
                    SuggestedStartUtc = suggestedStartUtc,
                    SuggestedLocalText = suggestedLocalText,
                    ReplyLastError = pendingRow.ReplyLastError
                });
            }

            return result;
        }

        public async Task<bool> RejectPendingReplyAsync(long emailProcessedId, CancellationToken ct)
        {
            var row = await _db.EmailProcessed
                .FirstOrDefaultAsync(item => item.Id == emailProcessedId, ct);

            if (row == null)
                return false;

            if (row.Replied)
                return false;

            if (!row.ReplyRequiresApproval)
                return false;

            var senderName = await GetSenderNameAsync(row.Provider, ct);
            var greeting = DetectGreetingFromDraft(row.ReplyDraftBody);

            var replyBody =
                greeting + "\n\n" +
                "Thank you for reaching out. I am unable to confirm the proposed slot.\n" +
                "Please share your availability and I will review it.\n\n" +
                "Best regards,\n" +
                senderName;

            replyBody = EnsureGreetingFirstLine(replyBody, greeting);

            await _emailClient.ReplyAsync(row.MessageId, replyBody.Trim(), ct);

            row.Replied = true;
            row.RepliedOn = DateTimeOffset.UtcNow;
            row.ReplyNeeded = false;
            row.ReplyRequiresApproval = false;
            row.ReplyDraftBody = null;
            row.WaitingForExternalConfirmation = false;
            row.ReplyLastError = "Rejected by user due to timing. Waiting for reschedule from sender.";
            SetProcessingStatus(row, ProcessingStatuses.ReschedulePending);

            await _db.SaveChangesAsync(ct);
            return true;
        }

        public async Task<bool> ApprovePendingReplyAsync(long emailProcessedId, bool useSuggestedSlot, CancellationToken ct)
        {
            var row = await _db.EmailProcessed.FirstOrDefaultAsync(item => item.Id == emailProcessedId, ct);
            if (row == null)
                return false;

            if (row.Replied)
                return true;

            if (!row.ReplyRequiresApproval)
                return false;

            var greeting = DetectGreetingFromDraft(row.ReplyDraftBody);
            var settings = await _db.ReminderAutomationSettings.FirstAsync(item => item.Id == 1, ct);
            var senderName = await GetSenderNameAsync(row.Provider, ct);

            var todayUtc = DateTime.UtcNow.Date;
            if (!settings.AiUsageDayUtc.HasValue || settings.AiUsageDayUtc.Value.Date != todayUtc)
            {
                settings.AiUsageDayUtc = todayUtc;
                settings.AiCallsToday = 0;
                settings.AiPausedUntilUtc = null;
                settings.AiLastError = null;
                await _db.SaveChangesAsync(ct);
            }

            if (settings.AiPausedUntilUtc.HasValue && settings.AiPausedUntilUtc.Value > DateTimeOffset.UtcNow)
                return false;

            if (settings.AiCallsToday >= Math.Max(1, settings.AiDailyLimit))
            {
                settings.AiPausedUntilUtc = DateTimeOffset.UtcNow.Date.AddDays(1);
                settings.AiLastError = "AI quota reached.";
                await _db.SaveChangesAsync(ct);
                return false;
            }

            string replyBody;

            if (!useSuggestedSlot)
            {
                if (!row.ProposedStartUtc.HasValue || !row.ProposedEndUtc.HasValue)
                    return false;

                var isOutsideOfficeHours = IsOutsideOfficeHours(
                    row.ProposedStartUtc.Value,
                    row.ProposedEndUtc.Value,
                    settings);

                if (!isOutsideOfficeHours)
                {
                    bool isFreeNow;
                    try
                    {
                        var hasReminderConflict = await HasReminderConflictAsync(
                            row.ProposedStartUtc.Value,
                            row.ProposedEndUtc.Value,
                            ct);

                        var calendarFree = await _calendar.IsFreeAsync(
                            row.ProposedStartUtc.Value,
                            row.ProposedEndUtc.Value,
                            settings,
                            ct);

                        isFreeNow = calendarFree && !hasReminderConflict;
                    }
                    catch (Exception ex)
                    {
                        row.ReplyLastError = "Calendar check failed during approval: " + ex.Message;
                        SetProcessingStatus(row, ProcessingStatuses.Error);
                        await _db.SaveChangesAsync(ct);
                        return false;
                    }

                    if (!isFreeNow)
                    {
                        row.ReplyLastError = "Original slot is busy. Please send the suggested slot instead.";
                        SetProcessingStatus(row, ProcessingStatuses.Error);
                        await _db.SaveChangesAsync(ct);
                        return false;
                    }
                }

                var sameSlotText = FormatLocal(row.ProposedStartUtc.Value, settings);

                replyBody =
                    greeting + "\n\n" +
                    "Thank you for reaching out. Yes, I can confirm the proposed slot of " + sameSlotText + ".\n" +
                    "Please proceed accordingly.\n\n" +
                    "Best regards,\n" +
                    senderName;

                var savedReminder = await CreateReminderForAcceptedSlotAsync(
                    row,
                    "Confirmed meeting",
                    "",
                    "",
                    ct);

                await _emailClient.ReplyAsync(row.MessageId, replyBody.Trim(), ct);

                if (savedReminder != null)
                {
                    await EnsureCalendarEventForReminderAsync(savedReminder, settings, ct);
                }

                row.WaitingForExternalConfirmation = false;
                row.ReplyLastError = "Original slot approved and confirmed.";
                SetProcessingStatus(row, ProcessingStatuses.Confirmed);
            }
            else
            {
                if (!row.SuggestedStartUtc.HasValue && string.IsNullOrWhiteSpace(row.SuggestedCalendarEventId))
                    return false;

                if (!string.IsNullOrWhiteSpace(row.SuggestedCalendarEventId))
                {
                    var latestEvent = await _calendar.GetEventSnapshotAsync(
                        row.SuggestedCalendarEventId,
                        settings,
                        ct);

                    if (latestEvent != null && latestEvent.StartUtc.HasValue)
                    {
                        row.SuggestedStartUtc = latestEvent.StartUtc;
                        row.SuggestedEndUtc = latestEvent.EndUtc;
                        row.SuggestedCalendarHtmlLink = latestEvent.HtmlLink;
                        await _db.SaveChangesAsync(ct);
                    }
                }

                if (!row.SuggestedStartUtc.HasValue)
                    return false;

                var suggestedEndUtcForReply =
                    row.SuggestedEndUtc ?? row.SuggestedStartUtc.Value.AddMinutes(settings.SlotMinutes);

                var suggestedRangeText =
                    FormatLocalRange(row.SuggestedStartUtc.Value, suggestedEndUtcForReply, settings);

                replyBody =
                    greeting + "\n\n" +
                    "Thank you for reaching out. I am unable to confirm the original proposed slot.\n" +
                    "Could we reschedule to " + suggestedRangeText + "?\n\n" +
                    "Best regards,\n" +
                    senderName;

                await _emailClient.ReplyAsync(row.MessageId, replyBody.Trim(), ct);

                row.WaitingForExternalConfirmation = true;
                row.ReplyLastError = "Suggested slot sent. Waiting for external confirmation.";
                SetProcessingStatus(row, ProcessingStatuses.WaitingSenderConfirmation);
            }

            row.Replied = true;
            row.RepliedOn = DateTimeOffset.UtcNow;
            row.ReplyNeeded = false;
            row.ReplyRequiresApproval = false;
            row.ReplyDraftBody = null;

            settings.AiCallsToday += 1;

            await _db.SaveChangesAsync(ct);
            return true;
        }
        public async Task<List<ProcessedEmailHistoryDto>> GetProcessedEmailHistoryAsync(CancellationToken ct)
        {
            var items = await _db.EmailProcessed
                .OrderByDescending(item => item.ProcessedOn)
                .Select(item => new ProcessedEmailHistoryDto
                {
                    Id = item.Id,
                    Provider = item.Provider,
                    MessageId = item.MessageId,
                    Subject = item.Subject ?? "",
                    From = item.From ?? "",
                    ProcessingStatus = item.ProcessingStatus ?? "",
                    Details = item.ReplyLastError ?? "",
                    ProposedStartUtc = item.ProposedStartUtc,
                    ProposedEndUtc = item.ProposedEndUtc,
                    SuggestedStartUtc = item.SuggestedStartUtc,
                    SuggestedEndUtc = item.SuggestedEndUtc,
                    ProcessedOn = item.ProcessedOn,
                    RepliedOn = item.RepliedOn
                })
                .ToListAsync(ct);

            return items;
        }
        #endregion

        #region Private helper methods
        private static string GetFullEmailText(EmailMessage email)
        {
            return ((email.Subject ?? "") + " " + (email.Snippet ?? "")).Trim();
        }

        private static string GetLatestReplyText(EmailMessage email)
        {
            var text = GetFullEmailText(email);

            if (string.IsNullOrWhiteSpace(text))
                return "";

            var splitMarkers = new[]
            {
        "\nOn ",
        "\r\nOn ",
        "\nFrom:",
        "\r\nFrom:",
        "\n-----Original Message-----",
        "\r\n-----Original Message-----",
        "\n________________________________",
        "\r\n________________________________",
        "\n> ",
        "\r\n> "
    };

            var latestPart = text;

            for (int markerIndex = 0; markerIndex < splitMarkers.Length; markerIndex++)
            {
                var marker = splitMarkers[markerIndex];
                var markerPosition = latestPart.IndexOf(marker, StringComparison.OrdinalIgnoreCase);

                if (markerPosition >= 0)
                {
                    latestPart = latestPart.Substring(0, markerPosition).Trim();
                }
            }

            return latestPart.Trim();
        }

        private static bool IsCancellationRequest(EmailMessage email)
        {
            var text = GetFullEmailText(email).ToLowerInvariant();

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
                text.Contains("has been cancelled") ||
                text.Contains("has been canceled") ||
                text.Contains("meeting cancelled") ||
                text.Contains("meeting canceled") ||
                text.Contains("meeting cancellation") ||
                text.Contains("interview cancelled") ||
                text.Contains("interview canceled") ||
                text.Contains("interview cancellation") ||
                text.Contains("event cancelled") ||
                text.Contains("event canceled") ||
                text.Contains("event cancellation") ||
                text.Contains("removed from google calendar") ||
                text.Contains("this event has been cancelled") ||
                text.Contains("this event has been canceled");
        }

        private static bool IsRescheduleRequest(EmailMessage email)
        {
            // Only latest reply text should be used here.
            // Old quoted thread text must not push a confirmation mail back into reschedule flow.
            var text = GetLatestReplyText(email).ToLowerInvariant();

            if (string.IsNullOrWhiteSpace(text))
                return false;

            return
                text.Contains("reschedule") ||
                text.Contains("rescheduled") ||
                text.Contains("rescheduling") ||
                text.Contains("schedule has changed") ||
                text.Contains("proposed new time") ||
                text.Contains("new time") ||
                text.Contains("updated time") ||
                text.Contains("change of schedule") ||
                text.Contains("can we move") ||
                text.Contains("can we shift") ||
                text.Contains("could we reschedule to");
        }


        private static string DetectReplyGreeting(EmailMessage email)
        {
            var latestReplyText = GetLatestReplyText(email);

            if (string.IsNullOrWhiteSpace(latestReplyText))
                return "Hello";

            var lines = latestReplyText
                .Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
                .Select(line => line.Trim())
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .ToList();

            if (lines.Count == 0)
                return "Hello";

            var firstLine = lines[0];

            if (Regex.IsMatch(firstLine, @"^(assalamu alaikum|assalam o alaikum|assalamualaikum|asalam o alaikum|aoa|salam)\b", RegexOptions.IgnoreCase))
                return "Wa Alaikum Assalam";

            if (Regex.IsMatch(firstLine, @"^dear\b", RegexOptions.IgnoreCase))
                return "Dear";

            if (Regex.IsMatch(firstLine, @"^hello\b", RegexOptions.IgnoreCase))
                return "Hello";

            if (Regex.IsMatch(firstLine, @"^hi\b", RegexOptions.IgnoreCase))
                return "Hi";

            if (Regex.IsMatch(firstLine, @"^hey\b", RegexOptions.IgnoreCase))
                return "Hey";

            return "Hello";
        }

        private static string EnsureGreetingFirstLine(string replyBody, string greeting)
        {
            if (string.IsNullOrWhiteSpace(replyBody))
                return greeting + Environment.NewLine;

            var trimmed = replyBody.TrimStart();

            if (trimmed.StartsWith("Wa Alaikum Assalam", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("Dear", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("Hello", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("Hi", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("Hey", StringComparison.OrdinalIgnoreCase))
            {
                return replyBody.Trim();
            }

            return greeting + Environment.NewLine + Environment.NewLine + replyBody.Trim();
        }

        private static bool IsOutsideOfficeHours(DateTimeOffset startUtc, DateTimeOffset endUtc, ReminderAutomationSettings settings)
        {
            var targetTimeZone = AppTimeHelper.ResolveTimeZone(settings.TimezoneId);

            var startLocal = TimeZoneInfo.ConvertTime(startUtc, targetTimeZone).DateTime;
            var endLocal = TimeZoneInfo.ConvertTime(endUtc, targetTimeZone).DateTime;

            var officeStartLocal = startLocal.Date.AddHours(settings.OfficeStartHour);
            var officeEndLocal = startLocal.Date.AddHours(settings.OfficeEndHour);

            if (startLocal < officeStartLocal)
                return true;

            if (endLocal > officeEndLocal)
                return true;

            return false;
        }

        private static string GetTimeZoneLabel(ReminderAutomationSettings settings)
        {
            if (string.IsNullOrWhiteSpace(settings.TimezoneId))
                return "local time";

            if (settings.TimezoneId.Equals("Asia/Karachi", StringComparison.OrdinalIgnoreCase) ||
                settings.TimezoneId.Equals("Pakistan Standard Time", StringComparison.OrdinalIgnoreCase))
                return "PKT";

            return settings.TimezoneId;
        }

        private static string FormatLocal(DateTimeOffset utc, ReminderAutomationSettings settings)
        {
            var localText = AppTimeHelper.FormatUtcAsLocal(utc, settings.TimezoneId, "dddd, MMM d, h:mm tt");
            return localText + " " + GetTimeZoneLabel(settings);
        }

        private static string FormatLocalRange(DateTimeOffset startUtc, DateTimeOffset endUtc, ReminderAutomationSettings settings)
        {
            var targetTimeZone = AppTimeHelper.ResolveTimeZone(settings.TimezoneId);

            var startLocal = TimeZoneInfo.ConvertTime(startUtc, targetTimeZone).DateTime;
            var endLocal = TimeZoneInfo.ConvertTime(endUtc, targetTimeZone).DateTime;

            var timeZoneLabel = GetTimeZoneLabel(settings);

            if (startLocal.Date == endLocal.Date)
            {
                return startLocal.ToString("dddd, MMM d, yyyy h:mm tt") +
                       " - " +
                       endLocal.ToString("h:mm tt") +
                       " " +
                       timeZoneLabel;
            }

            return startLocal.ToString("dddd, MMM d, yyyy h:mm tt") +
                   " - " +
                   endLocal.ToString("dddd, MMM d, yyyy h:mm tt") +
                   " " +
                   timeZoneLabel;
        }

        private static bool TryGetProposedUtcRange(
    EmailMessage email,
    ReminderAutomationSettings settings,
    out DateTimeOffset startUtc,
    out DateTimeOffset endUtc)
        {
            startUtc = default;
            endUtc = default;

            // Case 1:
            // Structured calendar invite already contains proper UTC values.
            // This is the most reliable source, so always trust it first.
            if (email.HasCalendarInvite && email.InviteStartUtc.HasValue && email.InviteEndUtc.HasValue)
            {
                startUtc = email.InviteStartUtc.Value.ToUniversalTime();
                endUtc = email.InviteEndUtc.Value.ToUniversalTime();
                return endUtc > startUtc;
            }

            // Use complete text instead of scattered subject/snippet patterns.
            if (TryExtractInviteStyleUtcRange(email, settings, out startUtc, out endUtc))
                return true;

            if (TryExtractProposedUtcRange(email, settings, out startUtc, out endUtc))
                return true;

            return false;
        }

        private static bool TryExtractProposedUtcRange(
    EmailMessage email,
    ReminderAutomationSettings settings,
    out DateTimeOffset startUtc,
    out DateTimeOffset endUtc)
        {
            startUtc = default;
            endUtc = default;

            var text = GetFullEmailText(email);
            if (string.IsNullOrWhiteSpace(text))
                return false;

            // Pattern 1:
            // between 10:00 AM and 11:00 AM on April 12
            var betweenMatch = Regex.Match(
                text,
                @"between\s+(?<start>\d{1,2}:\d{2}\s?(?:AM|PM))\s+and\s+(?<end>\d{1,2}:\d{2}\s?(?:AM|PM)).*?(?<date>(January|February|March|April|May|June|July|August|September|October|November|December)\s+\d{1,2}(?:,\s*\d{4})?)",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);

            if (betweenMatch.Success)
            {
                var dateText = betweenMatch.Groups["date"].Value.Trim();
                var startText = betweenMatch.Groups["start"].Value.Trim();
                var endText = betweenMatch.Groups["end"].Value.Trim();

                if (TryBuildUtcRangeFromLocalParts(dateText, startText, endText, settings, out startUtc, out endUtc))
                    return true;
            }

            // Pattern 2:
            // April 12, 2026 10:00 AM - 11:00 AM
            var directRangeMatch = Regex.Match(
                text,
                @"(?<date>(January|February|March|April|May|June|July|August|September|October|November|December)\s+\d{1,2}(?:,\s*\d{4})?)\s*(?:at\s*)?(?<start>\d{1,2}:\d{2}\s?(?:AM|PM))\s*[-–]\s*(?<end>\d{1,2}:\d{2}\s?(?:AM|PM))",
                RegexOptions.IgnoreCase);

            if (directRangeMatch.Success)
            {
                var dateText = directRangeMatch.Groups["date"].Value.Trim();
                var startText = directRangeMatch.Groups["start"].Value.Trim();
                var endText = directRangeMatch.Groups["end"].Value.Trim();

                if (TryBuildUtcRangeFromLocalParts(dateText, startText, endText, settings, out startUtc, out endUtc))
                    return true;
            }

            // Pattern 3:
            // 12 Apr 2026 2:00pm - 2:30pm
            var shortMonthRangeMatch = Regex.Match(
                text,
                @"(?<date>\d{1,2}\s+(Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec)\s+\d{4})\s+(?<start>\d{1,2}(?::\d{2})?\s?(?:am|pm))\s*[-–]\s*(?<end>\d{1,2}(?::\d{2})?\s?(?:am|pm))",
                RegexOptions.IgnoreCase);

            if (shortMonthRangeMatch.Success)
            {
                var dateText = shortMonthRangeMatch.Groups["date"].Value.Trim();
                var startText = shortMonthRangeMatch.Groups["start"].Value.Trim();
                var endText = shortMonthRangeMatch.Groups["end"].Value.Trim();

                if (TryBuildUtcRangeFromLocalParts(dateText, startText, endText, settings, out startUtc, out endUtc))
                    return true;
            }

            return false;
        }

        private async Task<bool> HasReminderConflictAsync(DateTimeOffset startUtc, DateTimeOffset endUtc, CancellationToken ct)
        {
            return await _db.Reminder.AnyAsync(item =>
                !item.Completed &&
                item.Type != ReminderType.Email &&
                item.ReminderTime >= startUtc &&
                item.ReminderTime < endUtc, ct);
        }

        private async Task<string> GetSenderNameAsync(string provider, CancellationToken ct)
        {
            var account = await _db.EmailOAuthAccounts
                .Where(item => item.Provider == provider && item.Active)
                .OrderByDescending(item => item.Id)
                .FirstOrDefaultAsync(ct);

            var senderName =
                !string.IsNullOrWhiteSpace(account?.DisplayName)
                    ? account.DisplayName.Trim()
                    : (!string.IsNullOrWhiteSpace(account?.Email) ? account.Email.Trim() : "SmartAssistant User");

            return senderName;
        }

        private async Task<DateTimeOffset?> MoveToNextReminderSafeSlotAsync(
            DateTimeOffset candidateStartUtc,
            ReminderAutomationSettings settings,
            CancellationToken ct)
        {
            var current = candidateStartUtc;

            for (int attemptIndex = 0; attemptIndex < 20; attemptIndex++)
            {
                var endUtc = current.AddMinutes(settings.SlotMinutes);

                var hasReminderConflict = await HasReminderConflictAsync(current, endUtc, ct);
                if (!hasReminderConflict)
                    return current;

                current = current.AddMinutes(settings.SlotMinutes);
            }

            return null;
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

        private static string NormalizeCancellationSubject(string? subject)
        {
            if (string.IsNullOrWhiteSpace(subject))
                return "";

            var value = subject.Trim().ToLowerInvariant();

            value = value.Replace("cancelled event:", "").Trim();
            value = value.Replace("canceled event:", "").Trim();
            value = value.Replace("updated invitation:", "").Trim();
            value = value.Replace("updated event:", "").Trim();

            var atIndex = value.IndexOf(" @ ");
            if (atIndex > 0)
                value = value.Substring(0, atIndex).Trim();

            return value;
        }

        private static bool TryExtractInviteStyleUtcRange(
            EmailMessage email,
            ReminderAutomationSettings settings,
            out DateTimeOffset startUtc,
            out DateTimeOffset endUtc)
        {
            startUtc = default;
            endUtc = default;

            var text = ((email.Subject ?? "") + " " + (email.Snippet ?? "")).Trim();
            if (string.IsNullOrWhiteSpace(text))
                return false;

            var longFormatMatch = Regex.Match(
                text,
                @"(?:Monday|Tuesday|Wednesday|Thursday|Friday|Saturday|Sunday),\s+" +
                @"(January|February|March|April|May|June|July|August|September|October|November|December)\s+" +
                @"(\d{1,2})(?:,\s*(\d{4}))?\s*[.\-]\s*" +
                @"(\d{1,2}:\d{2}\s*(?:AM|PM|am|pm)?)\s*[-]\s*" +
                @"(\d{1,2}:\d{2}\s*(?:AM|PM|am|pm)?)",
                RegexOptions.IgnoreCase);

            if (longFormatMatch.Success)
            {
                var monthText = longFormatMatch.Groups[1].Value;
                var dayText = longFormatMatch.Groups[2].Value;
                var yearText = longFormatMatch.Groups[3].Value;
                var startTimeText = longFormatMatch.Groups[4].Value;
                var endTimeText = longFormatMatch.Groups[5].Value;

                var year = string.IsNullOrWhiteSpace(yearText)
                    ? DateTimeOffset.UtcNow.Year
                    : int.Parse(yearText);

                if (DateTime.TryParse($"{monthText} {dayText} {year} {startTimeText}", out var localStart) &&
                    DateTime.TryParse($"{monthText} {dayText} {year} {endTimeText}", out var localEnd))
                {
                    startUtc = AppTimeHelper.ConvertLocalDateTimeToUtc(localStart, settings.TimezoneId);
                    endUtc = AppTimeHelper.ConvertLocalDateTimeToUtc(localEnd, settings.TimezoneId);

                    return endUtc > startUtc;
                }
            }

            var shortFormatMatch = Regex.Match(
                text,
                @"(?:Mon|Tue|Wed|Thu|Fri|Sat|Sun)\s+" +
                @"(\d{1,2})\s+" +
                @"(Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec)\s+" +
                @"(\d{4})\s+" +
                @"(\d{1,2}(?::\d{2})?\s*(?:am|pm))\s*[-]\s*" +
                @"(\d{1,2}(?::\d{2})?\s*(?:am|pm))\s*" +
                @"\(GMT([+-]\d{1,2})\)",
                RegexOptions.IgnoreCase);

            if (shortFormatMatch.Success)
            {
                var dayText = shortFormatMatch.Groups[1].Value;
                var monthText = shortFormatMatch.Groups[2].Value;
                var yearText = shortFormatMatch.Groups[3].Value;
                var startTimeText = shortFormatMatch.Groups[4].Value;
                var endTimeText = shortFormatMatch.Groups[5].Value;
                var offsetHoursText = shortFormatMatch.Groups[6].Value;

                var monthMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Jan"] = 1,
                    ["Feb"] = 2,
                    ["Mar"] = 3,
                    ["Apr"] = 4,
                    ["May"] = 5,
                    ["Jun"] = 6,
                    ["Jul"] = 7,
                    ["Aug"] = 8,
                    ["Sep"] = 9,
                    ["Oct"] = 10,
                    ["Nov"] = 11,
                    ["Dec"] = 12
                };

                if (!int.TryParse(dayText, out var day))
                    return false;

                if (!int.TryParse(yearText, out var year))
                    return false;

                if (!monthMap.TryGetValue(monthText, out var month))
                    return false;

                if (!int.TryParse(offsetHoursText, out var offsetHours))
                    return false;

                if (!DateTime.TryParse($"{year}-{month:D2}-{day:D2} {startTimeText}", out var localStart))
                    return false;

                if (!DateTime.TryParse($"{year}-{month:D2}-{day:D2} {endTimeText}", out var localEnd))
                    return false;

                var explicitOffset = TimeSpan.FromHours(offsetHours);

                var startWithOffset = new DateTimeOffset(
                    DateTime.SpecifyKind(localStart, DateTimeKind.Unspecified),
                    explicitOffset);

                var endWithOffset = new DateTimeOffset(
                    DateTime.SpecifyKind(localEnd, DateTimeKind.Unspecified),
                    explicitOffset);

                startUtc = startWithOffset.ToUniversalTime();
                endUtc = endWithOffset.ToUniversalTime();

                return endUtc > startUtc;
            }

            return false;
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

        private static bool IsFinalConfirmationEmail(EmailMessage email)
        {
            // Only latest reply text should be checked here.
            // Quoted thread history must not override a real confirmation reply.
            var text = GetLatestReplyText(email).ToLowerInvariant().Trim();

            if (string.IsNullOrWhiteSpace(text))
                return false;

            var hasPositiveSignal =
                text.Contains("confirmed") ||
                text.Contains("confirming") ||
                text.Contains("confirm this") ||
                text.Contains("that works") ||
                text.Contains("works for me") ||
                text.Contains("sounds good") ||
                text.Contains("okay for me") ||
                text.Contains("ok for me") ||
                text.Contains("fine with me") ||
                text.Contains("friday is fine") ||
                text.Contains("monday is fine") ||
                text.Contains("tuesday is fine") ||
                text.Contains("wednesday is fine") ||
                text.Contains("thursday is fine") ||
                text.Contains("saturday is fine") ||
                text.Contains("sunday is fine") ||
                text.Contains("yes, friday is fine") ||
                text.Contains("yes friday is fine") ||
                text.Contains("yes, that is fine") ||
                text.Contains("yes that is fine") ||
                text.Contains("yes, that's fine") ||
                text.Contains("yes that's fine") ||
                text.Contains("yes, fine") ||
                text.Contains("yes fine") ||
                text.Contains("let's do it") ||
                text.Contains("lets do it") ||
                text.Contains("see you then") ||
                text.Contains("i agree") ||
                text.Contains("agreed") ||
                text.Contains("accepted") ||
                text.Contains("yes, that works") ||
                text.Contains("yes that works") ||
                text.Contains("yes, confirmed") ||
                text.Contains("yes confirmed") ||
                text.Equals("confirmed") ||
                text.Equals("ok") ||
                text.Equals("okay") ||
                text.Equals("agreed");

            if (!hasPositiveSignal)
                return false;

            if (IsCancellationRequest(email))
                return false;

            return true;
        }

        private async Task<EmailProcessed> GetOrCreateEmailProcessedRowAsync(EmailMessage email, CancellationToken ct)
        {
            var existing = await _db.EmailProcessed
                .FirstOrDefaultAsync(item => item.Provider == email.Provider && item.MessageId == email.Id, ct);

            if (existing != null)
            {
                var changed = false;

                if (string.IsNullOrWhiteSpace(existing.Subject) && !string.IsNullOrWhiteSpace(email.Subject))
                {
                    existing.Subject = email.Subject.Trim();
                    changed = true;
                }

                if (string.IsNullOrWhiteSpace(existing.From) && !string.IsNullOrWhiteSpace(email.From))
                {
                    existing.From = email.From.Trim();
                    changed = true;
                }

                if (string.IsNullOrWhiteSpace(existing.CalendarEventId) && !string.IsNullOrWhiteSpace(email.CalendarEventId))
                {
                    existing.CalendarEventId = email.CalendarEventId;
                    changed = true;
                }

                if (changed)
                {
                    await _db.SaveChangesAsync(ct);
                }

                return existing;
            }

            var row = new EmailProcessed
            {
                Provider = email.Provider,
                MessageId = email.Id,
                ProcessedOn = DateTimeOffset.UtcNow,
                Subject = string.IsNullOrWhiteSpace(email.Subject) ? null : email.Subject.Trim(),
                From = string.IsNullOrWhiteSpace(email.From) ? null : email.From.Trim(),
                CalendarEventId = email.CalendarEventId
            };

            _db.EmailProcessed.Add(row);
            await _db.SaveChangesAsync(ct);
            return row;
        }

        private static bool LooksLikeInviteWithTime(EmailMessage email)
        {
            var text = GetFullEmailText(email).ToLowerInvariant();

            if (string.IsNullOrWhiteSpace(text))
                return false;

            var hasMeetingWords =
                text.Contains("interview") ||
                text.Contains("meeting") ||
                text.Contains("invite") ||
                text.Contains("invitation") ||
                text.Contains("call") ||
                text.Contains("appointment") ||
                text.Contains("schedule");

            var hasDateOrTimeWords =
                Regex.IsMatch(text, @"\b(mon|tue|wed|thu|fri|sat|sun)\b", RegexOptions.IgnoreCase) ||
                Regex.IsMatch(text, @"\b(jan|feb|mar|apr|may|jun|jul|aug|sep|oct|nov|dec)\b", RegexOptions.IgnoreCase) ||
                Regex.IsMatch(text, @"\b\d{1,2}:\d{2}\s*(am|pm)\b", RegexOptions.IgnoreCase) ||
                Regex.IsMatch(text, @"\bgmt[+-]?\d{1,2}\b", RegexOptions.IgnoreCase);

            return hasMeetingWords && hasDateOrTimeWords;
        }

        private async Task<Reminder> AddConfirmedReminderAsync(Reminder reminder, CancellationToken ct)
        {
            if (reminder.Id == Guid.Empty)
                reminder.Id = Guid.NewGuid();

            if (reminder.CreatedOn == default)
                reminder.CreatedOn = DateTime.UtcNow;

            reminder.Completed = false;

            _db.Reminder.Add(reminder);
            await _db.SaveChangesAsync(ct);

            return reminder;
        }

        private static string DetectGreetingFromDraft(string? replyDraftBody)
        {
            if (string.IsNullOrWhiteSpace(replyDraftBody))
                return "Hello";

            var lines = replyDraftBody
                .Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
                .Select(line => line.Trim())
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .ToList();

            if (lines.Count == 0)
                return "Hello";

            var firstLine = lines[0];

            if (Regex.IsMatch(firstLine, @"^wa alaikum assalam\b", RegexOptions.IgnoreCase))
                return "Wa Alaikum Assalam";

            if (Regex.IsMatch(firstLine, @"^dear\b", RegexOptions.IgnoreCase))
                return "Dear";

            if (Regex.IsMatch(firstLine, @"^hello\b", RegexOptions.IgnoreCase))
                return "Hello";

            if (Regex.IsMatch(firstLine, @"^hi\b", RegexOptions.IgnoreCase))
                return "Hi";

            if (Regex.IsMatch(firstLine, @"^hey\b", RegexOptions.IgnoreCase))
                return "Hey";

            return "Hello";
        }

        private async Task<Reminder?> CreateReminderForAcceptedSlotAsync(
            EmailProcessed processedRow,
            string subject,
            string snippet,
            string from,
            CancellationToken ct)
        {
            var finalStartUtc = processedRow.ProposedStartUtc ?? processedRow.SuggestedStartUtc;
            if (!finalStartUtc.HasValue)
                return null;

            var existingReminder = await _db.Reminder.FirstOrDefaultAsync(item =>
                item.SourceProvider == processedRow.Provider &&
                item.SourceId == processedRow.MessageId, ct);

            if (existingReminder != null)
                return existingReminder;

            var reminderTitle = string.IsNullOrWhiteSpace(subject)
                ? "Confirmed meeting"
                : subject.Trim();

            var reminderDescription =
                "Confirmed meeting from email." + Environment.NewLine + Environment.NewLine +
                "Source message id: " + processedRow.MessageId;

            if (!string.IsNullOrWhiteSpace(from))
            {
                reminderDescription += Environment.NewLine + "From: " + from;
            }

            if (!string.IsNullOrWhiteSpace(snippet))
            {
                reminderDescription += Environment.NewLine + Environment.NewLine + snippet;
            }

            var reminder = new Reminder
            {
                Title = reminderTitle,
                Description = reminderDescription,
                ReminderTime = finalStartUtc.Value,
                Type = ReminderType.Email,
                SourceProvider = processedRow.Provider,
                SourceId = processedRow.MessageId
            };

            return await AddConfirmedReminderAsync(reminder, ct);
        }

        private static string NormalizeSubjectForMatch(string? subject)
        {
            if (string.IsNullOrWhiteSpace(subject))
                return "";

            var value = subject.Trim().ToLowerInvariant();

            value = value.Replace("re:", "").Trim();
            value = value.Replace("fw:", "").Trim();
            value = value.Replace("fwd:", "").Trim();

            return value;
        }

        private async Task EnsureCalendarEventForReminderAsync(
            Reminder reminder,
            ReminderAutomationSettings settings,
            CancellationToken ct)
        {
            if (reminder == null)
                return;

            if (!string.IsNullOrWhiteSpace(reminder.CalendarEventId))
                return;

            try
            {
                var eventId = await _calendar.CreateEventAsync(reminder, settings, ct);
                reminder.CalendarEventId = eventId;
                reminder.CalendarSyncedOn = DateTimeOffset.UtcNow;
                reminder.CalendarSyncError = null;
            }
            catch (Exception ex)
            {
                reminder.CalendarSyncError = ex.Message;
            }

            await _db.SaveChangesAsync(ct);
        }

        private static bool IsSuggestedSlotDraft(string? replyDraftBody)
        {
            if (string.IsNullOrWhiteSpace(replyDraftBody))
                return false;

            var text = replyDraftBody.ToLowerInvariant();

            return
                text.Contains("could we reschedule to") ||
                text.Contains("unable to confirm the original proposed slot");
        }
        private static void SetProcessingStatus(EmailProcessed row, string status)
        {
            row.ProcessingStatus = status;
        }
        private static bool TryBuildUtcRangeFromLocalParts(
    string dateText,
    string startTimeText,
    string endTimeText,
    ReminderAutomationSettings settings,
    out DateTimeOffset startUtc,
    out DateTimeOffset endUtc)
        {
            startUtc = default;
            endUtc = default;

            var culture = CultureInfo.InvariantCulture;

            var dateFormats = new[]
            {
        "MMMM d, yyyy",
        "MMMM d yyyy",
        "MMMM d",
        "d MMM yyyy",
        "dd MMM yyyy"
    };

            DateTime parsedDate = default;
            var parsedDateOk = false;

            for (int formatIndex = 0; formatIndex < dateFormats.Length; formatIndex++)
            {
                if (DateTime.TryParseExact(dateText, dateFormats[formatIndex], culture, DateTimeStyles.None, out parsedDate))
                {
                    parsedDateOk = true;
                    break;
                }
            }

            if (!parsedDateOk)
            {
                // Last fallback
                if (!DateTime.TryParse(dateText, culture, DateTimeStyles.None, out parsedDate))
                    return false;
            }

            // If year was missing, assume current year.
            if (parsedDate.Year == 1)
            {
                parsedDate = new DateTime(
                    DateTime.UtcNow.Year,
                    parsedDate.Month,
                    parsedDate.Day
                );
            }

            if (!DateTime.TryParse(parsedDate.ToString("yyyy-MM-dd", culture) + " " + startTimeText, culture, DateTimeStyles.None, out var localStart))
                return false;

            if (!DateTime.TryParse(parsedDate.ToString("yyyy-MM-dd", culture) + " " + endTimeText, culture, DateTimeStyles.None, out var localEnd))
                return false;

            startUtc = AppTimeHelper.ConvertLocalDateTimeToUtc(
                DateTime.SpecifyKind(localStart, DateTimeKind.Unspecified),
                settings.TimezoneId);

            endUtc = AppTimeHelper.ConvertLocalDateTimeToUtc(
                DateTime.SpecifyKind(localEnd, DateTimeKind.Unspecified),
                settings.TimezoneId);

            return endUtc > startUtc;
        }
        private static bool IsMatchingConfirmationForPendingRow(EmailMessage email, EmailProcessed candidateRow)
        {
            if (candidateRow == null)
                return false;

            if (!candidateRow.WaitingForExternalConfirmation)
                return false;

            if (string.IsNullOrWhiteSpace(email.Provider) || string.IsNullOrWhiteSpace(candidateRow.Provider))
                return false;

            if (!string.Equals(email.Provider, candidateRow.Provider, StringComparison.OrdinalIgnoreCase))
                return false;

            if (!string.IsNullOrWhiteSpace(email.CalendarEventId))
            {
                if (string.Equals(email.CalendarEventId, candidateRow.CalendarEventId, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(email.CalendarEventId, candidateRow.SuggestedCalendarEventId, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            var incomingSubject = NormalizeSubjectForMatch(email.Subject);
            var candidateSubject = NormalizeSubjectForMatch(candidateRow.Subject);

            var subjectMatches =
                !string.IsNullOrWhiteSpace(incomingSubject) &&
                !string.IsNullOrWhiteSpace(candidateSubject) &&
                string.Equals(incomingSubject, candidateSubject, StringComparison.OrdinalIgnoreCase);

            var fromMatches =
                !string.IsNullOrWhiteSpace(email.From) &&
                !string.IsNullOrWhiteSpace(candidateRow.From) &&
                string.Equals(email.From.Trim(), candidateRow.From.Trim(), StringComparison.OrdinalIgnoreCase);

            if (subjectMatches && fromMatches)
                return true;

            if (subjectMatches)
                return true;

            return false;
        }
        #endregion
    }
}