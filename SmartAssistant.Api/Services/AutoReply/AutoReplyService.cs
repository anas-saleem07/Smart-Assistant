using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SmartAssistant.Api.Data;
using SmartAssistant.Api.Services.Ai;
using SmartAssistant.Api.Services.Calendar;
using SmartAssistant.Api.Services.Email;
using SmartAssistant.Core.Entities;

namespace SmartAssistant.Api.Services.AutoReply
{
    public interface IAutoReplyService
    {
        Task<bool> TryAutoReplyAsync(EmailMessage email, ReminderAutomationSettings settings, CancellationToken ct);
        Task<bool> SendApprovedReplyAsync(long emailProcessedId, CancellationToken ct);
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

            // Only reply if keywords match
            var replyKeywords = ParseKeywords(settings.AutoReplyKeywordsCsv);
            if (!IsMatch(email, replyKeywords))
                return false;

            // Find EmailProcessed row (created by your scan job)
            var processed = await _db.EmailProcessed
                .FirstOrDefaultAsync(x => x.Provider == email.Provider && x.MessageId == email.Id, ct);

            if (processed == null)
                return false;

            // If already replied, stop
            if (processed.Replied)
                return true;

            // Queue reply as soon as we decide it qualifies.
            if (!processed.ReplyNeeded)
            {
                processed.ReplyNeeded = true;
                processed.ReplyQueuedOn = DateTimeOffset.UtcNow;
                processed.ReplyLastError = null;
                await _db.SaveChangesAsync(ct);
            }

            // Daily quota reset (UTC date)
            var todayUtc = DateTime.UtcNow.Date;
            if (!settings.AiUsageDayUtc.HasValue || settings.AiUsageDayUtc.Value.Date != todayUtc)
            {
                settings.AiUsageDayUtc = todayUtc;
                settings.AiCallsToday = 0;
                settings.AiPausedUntilUtc = null;
                settings.AiLastError = null;
                await _db.SaveChangesAsync(ct);
            }

            // If paused, keep it queued and exit
            if (settings.AiPausedUntilUtc.HasValue && settings.AiPausedUntilUtc.Value > DateTimeOffset.UtcNow)
            {
                processed.ReplyLastError = "AI paused until " + settings.AiPausedUntilUtc.Value.ToString("u");
                await _db.SaveChangesAsync(ct);
                return false;
            }

            // If quota reached, pause until tomorrow, keep queued
            if (settings.AiCallsToday >= Math.Max(1, settings.AiDailyLimit))
            {
                settings.AiPausedUntilUtc = DateTimeOffset.UtcNow.Date.AddDays(1);
                settings.AiLastError = "AI quota reached.";
                await _db.SaveChangesAsync(ct);

                processed.ReplyLastError = "AI quota reached. Will retry later.";
                await _db.SaveChangesAsync(ct);
                return false;
            }

            // Fetch OAuth linked Gmail account name for signature
            var account = await _db.EmailOAuthAccounts
                .Where(x => x.Provider == email.Provider && x.Active)
                .OrderByDescending(x => x.Id)
                .FirstOrDefaultAsync(ct);

            var senderName =
                !string.IsNullOrWhiteSpace(account?.DisplayName)
                    ? account.DisplayName.Trim()
                    : (!string.IsNullOrWhiteSpace(account?.Email) ? account.Email.Trim() : "SmartAssistant User");

            // Greeting detection
            var greeting = DetectReplyGreeting(email);

            // If email proposes a specific time, do calendar-driven reply
            if (TryExtractProposedUtcRange(email, settings, out var proposedStartUtc, out var proposedEndUtc))
            {
                processed.ProposedStartUtc = proposedStartUtc;
                processed.ProposedEndUtc = proposedEndUtc;
                await _db.SaveChangesAsync(ct);

                // If proposed time is after office hours -> do not auto send (approval workflow)
                if (IsOutsideOfficeHours(proposedStartUtc, proposedEndUtc, settings))
                {
                    // If business allows auto after hours, you can override
                    if (settings.AllowAutoReplyAfterOfficeHours)
                    {
                        // Continue to normal calendar checks and send
                    }
                    else
                    {
                        // Create a draft reply and require approval
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

                        // Do not send automatically
                        return false;
                    }
                }

                // Calendar check
                bool isFree;
                try
                {
                    isFree = await _calendar.IsFreeAsync(proposedStartUtc, proposedEndUtc, settings, ct);
                }
                catch (Exception ex)
                {
                    processed.ReplyLastError = "Calendar check failed: " + ex.Message;
                    await _db.SaveChangesAsync(ct);
                    return false;
                }

                if (isFree)
                {
                    // Confirm directly (no AI needed)
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

                    settings.AiCallsToday += 1; // Count this as one automated action (optional)
                    await _db.SaveChangesAsync(ct);

                    return true;
                }

                // Not free -> reschedule with ONE suggestion within office hours if possible
                DateTimeOffset? nextFreeUtc = null;
                try
                {
                    nextFreeUtc = await _calendar.FindNextFreeSlotAsync(DateTimeOffset.UtcNow, settings, ct);
                }
                catch
                {
                    // ignore
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

            // If no specific time was proposed, use AI (but keep it clean)
            var prompt =
                "Write a short professional email reply.\n" +
                "Rules:\n" +
                "- Match greeting style: if the email says Assalam/Salaam, start with 'Wa Alaikum Assalam'. If it says Hello/Hi, start with Hello.\n" +
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

        // Approval endpoint will call this
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

            // Fetch latest settings
            var settings = await _db.ReminderAutomationSettings.FirstAsync(x => x.Id == 1, ct);

            // Quota check (do not send if paused or limit reached)
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

            // Send reply using Gmail
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

        // -------------------------
        // Greeting helpers
        // -------------------------

        private static string DetectReplyGreeting(EmailMessage email)
        {
            var text = ((email.Subject ?? "") + " " + (email.Snippet ?? "")).ToLowerInvariant();

            if (text.Contains("assalam") || text.Contains("asalam") || text.Contains("salam") || text.Contains("aoa"))
                return "Wa Alaikum Assalam";

            if (text.Contains("hello") || text.Contains("hi") || text.Contains("hey"))
                return "Hello";

            return "Hello";
        }

        private static string EnsureGreetingFirstLine(string replyBody, string greeting)
        {
            if (string.IsNullOrWhiteSpace(replyBody))
                return greeting + Environment.NewLine;

            var trimmed = replyBody.TrimStart();

            if (trimmed.StartsWith(greeting, StringComparison.OrdinalIgnoreCase))
                return replyBody.Trim();

            return greeting + Environment.NewLine + Environment.NewLine + replyBody.Trim();
        }

        // -------------------------
        // Time helpers
        // -------------------------

        private static bool IsOutsideOfficeHours(DateTimeOffset startUtc, DateTimeOffset endUtc, ReminderAutomationSettings settings)
        {
            var tz = TimeZoneInfo.FindSystemTimeZoneById(settings.TimezoneId);

            var startLocal = TimeZoneInfo.ConvertTime(startUtc, tz).DateTime;
            var endLocal = TimeZoneInfo.ConvertTime(endUtc, tz).DateTime;

            // If it crosses office hours boundary or starts outside office range -> treat as outside
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

        // Supports your HR format: "March 5 between 10:00 AM and 11:00 AM"
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

        // -------------------------
        // Keyword helpers
        // -------------------------

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