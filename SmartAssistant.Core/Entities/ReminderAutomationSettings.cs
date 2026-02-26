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
    }

    public class EmailProcessed
    {
        public long Id { get; set; }
        public string Provider { get; set; } = "Gmail"; // or "Outlook"
        public string MessageId { get; set; } = default!;
        public DateTimeOffset ProcessedOn { get; set; } = DateTimeOffset.UtcNow;
    }
}