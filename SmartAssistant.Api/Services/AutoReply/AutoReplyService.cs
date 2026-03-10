using Microsoft.EntityFrameworkCore;
using SmartAssistant.Api.Data;
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

namespace SmartAssistant.Api.Services.AutoReply
{
    public interface IAutoReplyService
    {
        Task<bool> TryAutoReplyAsync(EmailMessage email, ReminderAutomationSettings settings, CancellationToken ct);
        Task<bool> SendApprovedReplyAsync(long emailProcessedId, CancellationToken ct);

        Task<List<PendingAutoReplyDto>> GetPendingApprovalsAsync(CancellationToken ct);
        Task<bool> ApprovePendingReplyAsync(long emailProcessedId, bool useSuggestedSlot, CancellationToken ct, EmailMessage email);
        Task<bool> RejectPendingReplyAsync(long emailProcessedId, CancellationToken ct, EmailMessage email);

        Task<ApprovalCalendarOpenDto> CreateSuggestedCalendarEventAsync(long emailProcessedId, CancellationToken ct);
    }

    public sealed class PendingAutoReplyDto
    {
        public long Id { get; set; }
        public string Provider { get; set; } = "";
        public string MessageId { get; set; } = "";
        public string Subject { get; set; } = "";
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
    public sealed class AutoReplyService : IAutoReplyService
    {
        private readonly ApplicationDbContext _db;
        private readonly IAiClient _ai;
        private readonly IEmailClient _emailClient;
        private readonly ICalendarService _calendar;

        public AutoReplyService(ApplicationDbContext db, IAiClient ai, IEmailClient emailClient, ICalendarService calendar)
        {
            _db = db;
            _ai = ai;
            _emailClient = emailClient;
            _calendar = calendar;
        }

        public async Task<bool> TryAutoReplyAsync(EmailMessage email, ReminderAutomationSettings settings, CancellationToken ct)
        {
            if (!settings.AutoReplyEnabled)
                return false;

            var replyKeywords = ParseKeywords(settings.AutoReplyKeywordsCsv);
            var hasStructuredInvite =
                email.HasCalendarInvite &&
                email.InviteStartUtc.HasValue &&
                email.InviteEndUtc.HasValue;

            if (!hasStructuredInvite && !IsMatch(email, replyKeywords))
                return false;

            var processed = await _db.EmailProcessed
                .FirstOrDefaultAsync(x => x.Provider == email.Provider && x.MessageId == email.Id, ct);

            if (processed == null)
                return false;

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
                await _db.SaveChangesAsync(ct);
                return false;
            }

            if (settings.AiCallsToday >= Math.Max(1, settings.AiDailyLimit))
            {
                settings.AiPausedUntilUtc = DateTimeOffset.UtcNow.Date.AddDays(1);
                settings.AiLastError = "AI quota reached.";
                await _db.SaveChangesAsync(ct);

                processed.ReplyLastError = "AI quota reached. Will retry later.";
                await _db.SaveChangesAsync(ct);
                return false;
            }

            var account = await _db.EmailOAuthAccounts
                .Where(x => x.Provider == email.Provider && x.Active)
                .OrderByDescending(x => x.Id)
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

                var tz = TimeZoneInfo.FindSystemTimeZoneById(settings.TimezoneId);
                var debugLocal = TimeZoneInfo.ConvertTime(proposedStartUtc, tz);

                processed.ReplyLastError = $"DEBUG UTC: {proposedStartUtc:u} | LOCAL: {debugLocal}";
                await _db.SaveChangesAsync(ct);

                if (IsOutsideOfficeHours(proposedStartUtc, proposedEndUtc, settings))
                {
                    if (settings.AllowAutoReplyAfterOfficeHours)
                    {
                    }
                    else
                    {
                        var draft =
                            greeting + "\n\n" +
                            "Thank you for reaching out. The proposed time is outside my regular office hours.\n" +
                            "If you would like, I can confirm this slot, or we can reschedule within office hours.\n" +
                            "Please let me know your preference.\n\n" +
                            "Best regards,\n" +
                            senderName;

                        processed.ReplyRequiresApproval = settings.RequireApprovalAfterOfficeHours;
                        processed.ReplyDraftBody = draft;
                        processed.ReplyLastError = "Approval required: proposed time is outside office hours.";
                        await _db.SaveChangesAsync(ct);

                        return false;
                    }
                }

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
                    await _db.SaveChangesAsync(ct);
                    return false;
                }

                if (isFree)
                {
                    if (!string.IsNullOrWhiteSpace(email.CalendarEventId))
                    {
                        var accepted = await _calendar.AcceptInviteAsync(email.CalendarEventId, settings, ct);
                        if (!accepted)
                        {
                            processed.ReplyLastError = "Calendar invite acceptance failed.";
                            await _db.SaveChangesAsync(ct);
                            return false;
                        }
                    }

                    var reply =
                        greeting + "\n\n" +
                        "Thank you for the invitation. Yes, I am available at the proposed time.\n" +
                        "Please confirm and I will join accordingly.\n\n" +
                        "Best regards,\n" +
                        senderName;

                    await _emailClient.ReplyAsync(email.Id, reply, ct);

                    processed.Replied = true;
                    processed.RepliedOn = DateTimeOffset.UtcNow;
                    processed.ReplyNeeded = false;
                    processed.ReplyLastError = null;

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

                var reply2 =
                    greeting + "\n\n" +
                    "Thank you for reaching out.\n" +
                    rescheduleLine + "\n\n" +
                    "Best regards,\n" +
                    senderName;

                await _emailClient.ReplyAsync(email.Id, reply2, ct);

                processed.Replied = true;
                processed.RepliedOn = DateTimeOffset.UtcNow;
                processed.ReplyNeeded = false;
                processed.ReplyLastError = null;

                settings.AiCallsToday += 1;
                await _db.SaveChangesAsync(ct);

                return true;
            }

            var prompt =
                "Write a short professional email reply.\n" +
                "Rules:\n" +
                "- If the email says Assalamu Alaikum / Assalam / Salaam / AOA, start with 'Wa Alaikum Assalam'.\n" +
                "- If the email says Dear, start with 'Dear'.\n" +
                "- If the email says Hello, start with 'Hello'.\n" +
                "- If the email says Hi, start with 'Hi'.\n" +
                "- If the email says Hey, start with 'Hey'.\n" +
                "- The email is about scheduling but does not contain a specific time.\n" +
                "- Say you are available during office hours and ask them to confirm a suitable time.\n" +
                "- Do not list multiple time slots.\n" +
                "- Keep it concise.\n" +
                "- Do NOT include placeholders.\n" +
                "- End with:\n" +
                "Best regards,\n" +
                senderName + "\n\n" +
                "Email subject: " + (email.Subject ?? "") + "\n" +
                "Email snippet: " + (email.Snippet ?? "") + "\n" +
                "Reply body only.";

            string replyBody;
            try
            {
                replyBody = await _ai.GenerateAsync(prompt, ct);
            }
            catch (Exception ex)
            {
                processed.ReplyLastError = "AI error: " + ex.Message;
                await _db.SaveChangesAsync(ct);
                return false;
            }

            if (string.IsNullOrWhiteSpace(replyBody))
            {
                processed.ReplyLastError = "AI returned empty reply.";
                await _db.SaveChangesAsync(ct);
                return false;
            }

            replyBody = EnsureGreetingFirstLine(replyBody, greeting);

            await _emailClient.ReplyAsync(email.Id, replyBody.Trim(), ct);

            processed.Replied = true;
            processed.RepliedOn = DateTimeOffset.UtcNow;
            processed.ReplyNeeded = false;
            processed.ReplyLastError = null;

            settings.AiCallsToday += 1;
            await _db.SaveChangesAsync(ct);

            return true;
        }

        public async Task<bool> SendApprovedReplyAsync(long emailProcessedId, CancellationToken ct)
        {
            var row = await _db.EmailProcessed.FirstOrDefaultAsync(x => x.Id == emailProcessedId, ct);
            if (row == null)
                return false;

            if (row.Replied)
                return true;

            if (!row.ReplyRequiresApproval)
                return false;

            if (string.IsNullOrWhiteSpace(row.ReplyDraftBody))
                return false;

            var settings = await _db.ReminderAutomationSettings.FirstAsync(x => x.Id == 1, ct);

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

            await _emailClient.ReplyAsync(row.MessageId, row.ReplyDraftBody.Trim(), ct);

            row.Replied = true;
            row.RepliedOn = DateTimeOffset.UtcNow;
            row.ReplyNeeded = false;
            row.ReplyRequiresApproval = false;
            row.ReplyLastError = null;

            settings.AiCallsToday += 1;

            await _db.SaveChangesAsync(ct);
            return true;
        }
        public async Task<ApprovalCalendarOpenDto> CreateSuggestedCalendarEventAsync(long emailProcessedId, CancellationToken ct)
        {
            var result = new ApprovalCalendarOpenDto
            {
                Success = false,
                Message = "Unable to create calendar event."
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

            if (!row.ProposedStartUtc.HasValue)
            {
                result.Message = "No proposed start time is available.";
                return result;
            }

            var settings = await _db.ReminderAutomationSettings.FirstAsync(item => item.Id == 1, ct);

            DateTimeOffset? suggestedStartUtc = await _calendar.FindNextFreeSlotOnSameDayAsync(
                row.ProposedStartUtc.Value,
                settings,
                ct);

            if (!suggestedStartUtc.HasValue)
            {
                suggestedStartUtc = await _calendar.FindNextFreeSlotAsync(
                    row.ProposedStartUtc.Value,
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

            var calendarCreateResult = await _calendar.CreateApprovalSuggestionEventAsync(
                suggestedStartUtc.Value,
                suggestedEndUtc,
                "Suggested meeting slot",
                "Created from SmartAssistant approval notification bar.",
                settings,
                ct);

            if (calendarCreateResult == null)
            {
                result.Message = "Google Calendar event could not be created.";
                return result;
            }

            row.SuggestedStartUtc = suggestedStartUtc.Value;
            row.SuggestedEndUtc = suggestedEndUtc;
            row.SuggestedCalendarEventId = calendarCreateResult.EventId;
            row.SuggestedCalendarHtmlLink = calendarCreateResult.EventHtmlLink;
            row.CalendarCreatedOn = DateTimeOffset.UtcNow;
            row.CalendarLastError = null;

            await _db.SaveChangesAsync(ct);

            result.Success = true;
            result.Message = "Google Calendar event created successfully.";
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

                if (pendingRow.ProposedStartUtc.HasValue)
                {
                    try
                    {
                        suggestedStartUtc = await _calendar.FindNextFreeSlotOnSameDayAsync(
                            pendingRow.ProposedStartUtc.Value,
                            settings,
                            ct);

                        if (!suggestedStartUtc.HasValue)
                        {
                            suggestedStartUtc = await _calendar.FindNextFreeSlotAsync(
                                pendingRow.ProposedStartUtc.Value,
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

                        if (suggestedStartUtc.HasValue)
                        {
                            suggestedLocalText = FormatLocal(suggestedStartUtc.Value, settings);
                        }
                    }
                    catch
                    {
                        suggestedStartUtc = null;
                        suggestedLocalText = "";
                    }
                }

                result.Add(new PendingAutoReplyDto
                {
                    Id = pendingRow.Id,
                    Provider = pendingRow.Provider ?? "",
                    MessageId = pendingRow.MessageId ?? "",
                    Subject = "Pending scheduling approval",
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

        public async Task<bool> ApprovePendingReplyAsync(long emailProcessedId, bool useSuggestedSlot, CancellationToken ct,EmailMessage email)
        {
            var row = await _db.EmailProcessed.FirstOrDefaultAsync(item => item.Id == emailProcessedId, ct);
            if (row == null)
                return false;

            if (row.Replied)
                return true;

            if (!row.ReplyRequiresApproval)
                return false;
            var greeting = DetectReplyGreeting(email);
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
                var sameSlotText = row.ProposedStartUtc.HasValue
                    ? FormatLocal(row.ProposedStartUtc.Value, settings)
                    : "the proposed time";

                replyBody =
                    greeting + "\n\n" +
                    "Thank you for reaching out. Yes, I can confirm the proposed slot of " + sameSlotText + ".\n" +
                    "Please proceed accordingly.\n\n" +
                    "Best regards,\n" +
                    senderName;
            }
            else
            {
                if (!row.SuggestedStartUtc.HasValue && string.IsNullOrWhiteSpace(row.SuggestedCalendarEventId))
                    return false;

                // Refresh from Google Calendar if event id exists,
                // because user may have edited the event manually after creation.
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

                replyBody =
                    greeting + "\n\n" +
                    "Thank you for reaching out. I am unable to confirm the original proposed slot.\n" +
                    "Could we reschedule to " + FormatLocal(row.SuggestedStartUtc.Value, settings) + "?\n\n" +
                    "Best regards,\n" +
                    senderName;
            }

            await _emailClient.ReplyAsync(row.MessageId, replyBody.Trim(), ct);

            row.Replied = true;
            row.RepliedOn = DateTimeOffset.UtcNow;
            row.ReplyNeeded = false;
            row.ReplyRequiresApproval = false;
            row.ReplyLastError = null;
            row.ReplyDraftBody = null;

            settings.AiCallsToday += 1;

            await _db.SaveChangesAsync(ct);
            return true;
        }

        public async Task<bool> RejectPendingReplyAsync(long emailProcessedId, CancellationToken ct,EmailMessage email)
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

            // We do not have the original EmailMessage object here,
            // so use available stored text to detect greeting style.
            var greeting = DetectReplyGreeting(email);

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
            row.ReplyLastError = null;

            await _db.SaveChangesAsync(ct);
            return true;
        }

        private static string DetectReplyGreeting(EmailMessage email)
        {
            var text = ((email.Subject ?? "") + " " + (email.Snippet ?? "")).ToLowerInvariant();

            if (text.Contains("assalamu alaikum") ||
                text.Contains("assalam o alaikum") ||
                text.Contains("assalamualaikum") ||
                text.Contains("assalam") ||
                text.Contains("asalam") ||
                text.Contains("salam") ||
                text.Contains("aoa"))
                return "Wa Alaikum Assalam";

            if (text.Contains("dear"))
                return "Dear";

            if (text.Contains("hello"))
                return "Hello";

            if (text.Contains("hi"))
                return "Hi";

            if (text.Contains("hey"))
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
            var tz = TimeZoneInfo.FindSystemTimeZoneById(settings.TimezoneId);

            var startLocal = TimeZoneInfo.ConvertTime(startUtc, tz).DateTime;
            var endLocal = TimeZoneInfo.ConvertTime(endUtc, tz).DateTime;

            var officeStart = startLocal.Date.AddHours(settings.OfficeStartHour);
            var officeEnd = startLocal.Date.AddHours(settings.OfficeEndHour);

            if (startLocal < officeStart)
                return true;

            if (endLocal > officeEnd)
                return true;

            return false;
        }

        private static string FormatLocal(DateTimeOffset utc, ReminderAutomationSettings settings)
        {
            var tz = TimeZoneInfo.FindSystemTimeZoneById(settings.TimezoneId);
            var local = TimeZoneInfo.ConvertTime(utc, tz);
            return local.ToString("dddd, MMM d, h:mm tt");
        }

        private static bool TryGetProposedUtcRange(
            EmailMessage email,
            ReminderAutomationSettings settings,
            out DateTimeOffset startUtc,
            out DateTimeOffset endUtc)
        {
            startUtc = default;
            endUtc = default;

            if (email.HasCalendarInvite && email.InviteStartUtc.HasValue && email.InviteEndUtc.HasValue)
            {
                var inviteStartUtc = email.InviteStartUtc.Value;
                var inviteEndUtc = email.InviteEndUtc.Value;

                var tz = TimeZoneInfo.FindSystemTimeZoneById(settings.TimezoneId);

                var localStart = TimeZoneInfo.ConvertTime(inviteStartUtc, tz);
                var localEnd = TimeZoneInfo.ConvertTime(inviteEndUtc, tz);

                if (localStart.Hour >= 0 && localStart.Hour <= 23)
                {
                    startUtc = inviteStartUtc;
                    endUtc = inviteEndUtc;
                    return endUtc > startUtc;
                }
            }

            if (TryExtractInviteStyleUtcRange(email, settings, out startUtc, out endUtc))
                return true;

            return TryExtractProposedUtcRange(email, settings, out startUtc, out endUtc);
        }

        private static bool TryExtractProposedUtcRange(
            EmailMessage email,
            ReminderAutomationSettings settings,
            out DateTimeOffset startUtc,
            out DateTimeOffset endUtc)
        {
            startUtc = default;
            endUtc = default;

            var text = (email.Snippet ?? "") + " " + (email.Subject ?? "");
            if (string.IsNullOrWhiteSpace(text))
                return false;

            var betweenMatch = Regex.Match(
                text,
                @"between\s+(\d{1,2}:\d{2}\s?(AM|PM))\s+and\s+(\d{1,2}:\d{2}\s?(AM|PM))",
                RegexOptions.IgnoreCase);

            if (!betweenMatch.Success)
                return false;

            var time1 = betweenMatch.Groups[1].Value;
            var time2 = betweenMatch.Groups[3].Value;

            var dateMatch = Regex.Match(
                text,
                @"(January|February|March|April|May|June|July|August|September|October|November|December)\s+\d{1,2}",
                RegexOptions.IgnoreCase);

            if (!dateMatch.Success)
                return false;

            var year = DateTimeOffset.UtcNow.Year;

            if (!DateTime.TryParse(dateMatch.Value + " " + year, out var date))
                return false;

            if (!DateTime.TryParse(date.ToString("yyyy-MM-dd") + " " + time1, out var localStart))
                return false;

            if (!DateTime.TryParse(date.ToString("yyyy-MM-dd") + " " + time2, out var localEnd))
                return false;

            var tz = TimeZoneInfo.FindSystemTimeZoneById(settings.TimezoneId);

            var startLocalOffset = new DateTimeOffset(localStart, tz.GetUtcOffset(localStart));
            var endLocalOffset = new DateTimeOffset(localEnd, tz.GetUtcOffset(localEnd));

            startUtc = startLocalOffset.ToUniversalTime();
            endUtc = endLocalOffset.ToUniversalTime();

            return endUtc > startUtc;
        }

        private async Task<bool> HasReminderConflictAsync(DateTimeOffset startUtc, DateTimeOffset endUtc, CancellationToken ct)
        {
            return await _db.Reminder.AnyAsync(x =>
                !x.Completed &&
                x.ReminderTime >= startUtc &&
                x.ReminderTime < endUtc, ct);
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

            for (int i = 0; i < 20; i++)
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
                .Select(k => k.Trim())
                .Where(k => k.Length > 0)
                .Select(k => k.ToLowerInvariant())
                .ToList();
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
                    var tz = TimeZoneInfo.FindSystemTimeZoneById(settings.TimezoneId);

                    var startOffset = new DateTimeOffset(localStart, tz.GetUtcOffset(localStart));
                    var endOffset = new DateTimeOffset(localEnd, tz.GetUtcOffset(localEnd));

                    startUtc = startOffset.ToUniversalTime();
                    endUtc = endOffset.ToUniversalTime();

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

                var offset = TimeSpan.FromHours(offsetHours);

                var startWithOffset = new DateTimeOffset(localStart, offset);
                var endWithOffset = new DateTimeOffset(localEnd, offset);

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

            for (int i = 0; i < keywords.Count; i++)
            {
                var k = keywords[i];
                if (!string.IsNullOrWhiteSpace(k) && haystack.Contains(k))
                    return true;
            }

            return false;
        }
    }
}