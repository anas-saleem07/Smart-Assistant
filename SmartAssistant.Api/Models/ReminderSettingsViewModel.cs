namespace SmartAssistant.Api.Models
{
    public sealed class ReminderSettingsViewModel
    {
        // Editable fields
        public bool Enabled { get; set; }

        public int ScanIntervalMinutes { get; set; }
        public int DefaultReminderAfterMinutes { get; set; }

        public string KeywordsCsv { get; set; } = string.Empty;
        public string GmailQuery { get; set; } = string.Empty;

        public bool AutoReplyEnabled { get; set; }
        public string AutoReplyKeywordsCsv { get; set; } = string.Empty;

        public int AiDailyLimit { get; set; }

        public int OfficeStartHour { get; set; }
        public int OfficeEndHour { get; set; }
        public int SlotMinutes { get; set; }
        public string TimezoneId { get; set; } = "Asia/Karachi";

        public bool CalendarEnabled { get; set; }
        public string? CalendarId { get; set; } = "primary";

        public bool RequireApprovalAfterOfficeHours { get; set; }
        public bool AllowAutoReplyAfterOfficeHours { get; set; }

        // Read-only status fields
        public DateTimeOffset? LastRunOn { get; set; }
        public int LastRunCreatedCount { get; set; }
        public string? LastRunStatus { get; set; }
        public string? LastRunError { get; set; }

        public int AiCallsToday { get; set; }
        public DateTime? AiUsageDayUtc { get; set; }
        public DateTimeOffset? AiPausedUntilUtc { get; set; }
        public string? AiLastError { get; set; }
    }
}