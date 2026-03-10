namespace SmartAssistant.Core.Entities
{
    public class ReminderAutomationSettings
    {
        public int Id { get; set; } = 1; // single-row settings table
        public bool Enabled { get; set; } = true;

        // how often the background worker should scan
        public int ScanIntervalMinutes { get; set; } = 10;

        // when we can't detect a due date, schedule reminder like "now + DefaultReminderAfterMinutes"
        public int DefaultReminderAfterMinutes { get; set; } = 60;

        // simple matching (can be replaced with NLP later)
        public string KeywordsCsv { get; set; } = "action required,urgent,asap,deadline,meeting,invoice,follow up";

        public string GmailQuery { get; set; } = "in:inbox newer_than:7d -category:promotions -category:social";

        public DateTimeOffset? LastRunOn { get; set; }
        public int LastRunCreatedCount { get; set; }
        public string? LastRunStatus { get; set; }   // "Success" / "Failed" / "Running"
        public string? LastRunError { get; set; }    // store last exception message (short)

        // Auto reply switches
        public bool AutoReplyEnabled { get; set; } = false;

        // Separate keywords for replying (do not mix with reminder keywords)
        public string AutoReplyKeywordsCsv { get; set; } = "interview,meeting,schedule,call,appointment";

        // AI quota gate (daily)
        public int AiDailyLimit { get; set; } = 50;
        public int AiCallsToday { get; set; } = 0;
        public DateTime? AiUsageDayUtc { get; set; }                  // stores DateTime.UtcNow.Date
        public DateTimeOffset? AiPausedUntilUtc { get; set; }         // when quota hit, pause until this time
        public string? AiLastError { get; set; }                      // last quota/error message

        // Office hours for slot suggestions (simple)
        public int OfficeStartHour { get; set; } = 9;
        public int OfficeEndHour { get; set; } = 18;
        public int SlotMinutes { get; set; } = 30;
        public string TimezoneId { get; set; } = "Asia/Karachi";

        // Calendar integration settings
        public bool CalendarEnabled { get; set; } = true;

        // Which Google calendar to use
        // "primary" means the user's main calendar
        public string? CalendarId { get; set; } = "primary";

        // Auto reply safety
        public bool RequireApprovalAfterOfficeHours { get; set; } = true;

        // If true, system can auto send even after office hours.
        // Keep false for production safety.
        public bool AllowAutoReplyAfterOfficeHours { get; set; } = false;

    }

    public class EmailProcessed
    {
        public long Id { get; set; }
        public string Provider { get; set; } = "Gmail"; // or "Outlook"
        public string MessageId { get; set; } = default!;
        public DateTimeOffset ProcessedOn { get; set; } = DateTimeOffset.UtcNow;

        public string? CalendarEventId { get; set; }
        public DateTimeOffset? CalendarCreatedOn { get; set; }
        public string? CalendarLastError { get; set; }

        public bool ReplyNeeded { get; set; }              // Email qualifies for reply but not yet replied
        public DateTimeOffset? ReplyQueuedOn { get; set; } // When we queued it
        public bool Replied { get; set; }                  // Reply actually sent
        public DateTimeOffset? RepliedOn { get; set; }     // When we replied
        public string? ReplyLastError { get; set; }        // Last failure reason (quota, api error, etc.)

        // Auto reply approval workflow (for after office hours)
        public bool ReplyRequiresApproval { get; set; } = false;
        public string? ReplyDraftBody { get; set; }              // Prepared reply waiting for approval
        public DateTimeOffset? ProposedStartUtc { get; set; }    // Extracted proposed start time (optional)
        public DateTimeOffset? ProposedEndUtc { get; set; }      // Extracted proposed end time (optional)

        public DateTimeOffset? SuggestedStartUtc { get; set; }
        public DateTimeOffset? SuggestedEndUtc { get; set; }
        public string? SuggestedCalendarEventId { get; set; }
        public string? SuggestedCalendarHtmlLink { get; set; }


    }
}