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
        public string AutoReplyKeywordsCsv { get; set; } = "interview,meeting,schedule,call,appointment,reschedule,rescheduled,cancel,cancelled,canceled";

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

            // NEW: which signed-in email account owns this processed row
            public string? AccountEmail { get; set; }

            public string? CalendarEventId { get; set; }
            public DateTimeOffset? CalendarCreatedOn { get; set; }
            public string? CalendarLastError { get; set; }

            public bool ReplyNeeded { get; set; }
            public DateTimeOffset? ReplyQueuedOn { get; set; }
            public bool Replied { get; set; }
            public bool WaitingForExternalConfirmation { get; set; }
            public DateTimeOffset? RepliedOn { get; set; }
            public string? ReplyLastError { get; set; }

            public bool ReplyRequiresApproval { get; set; } = false;
            public string? ReplyDraftBody { get; set; }
            public DateTimeOffset? ProposedStartUtc { get; set; }
            public DateTimeOffset? ProposedEndUtc { get; set; }

            public DateTimeOffset? SuggestedStartUtc { get; set; }
            public DateTimeOffset? SuggestedEndUtc { get; set; }
            public string? SuggestedCalendarEventId { get; set; }
            public string? SuggestedCalendarHtmlLink { get; set; }
            public string? ProposedTimezoneId { get; set; }

            public string? Subject { get; set; }
            public string? From { get; set; }
            public string? ProcessingStatus { get; set; }
        }
    }
