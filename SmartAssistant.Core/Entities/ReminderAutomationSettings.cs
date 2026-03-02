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
    }

    public class EmailProcessed
    {
        public long Id { get; set; }
        public string Provider { get; set; } = "Gmail"; // or "Outlook"
        public string MessageId { get; set; } = default!;
        public DateTimeOffset ProcessedOn { get; set; } = DateTimeOffset.UtcNow;
    }
}