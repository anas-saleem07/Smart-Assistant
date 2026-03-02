namespace SmartAssistant.Core.Entities
{
    public enum ReminderType
    {
        Manual = 1,
        Email = 2
    }

    public class Reminder
    {
        public Guid Id { get; set; }
        public string? Title { get; set; }
        public string? Description { get; set; }
        public DateTimeOffset ReminderTime { get; set; }
        public bool Completed { get; set; }
        public DateTime CreatedOn { get; set; }

        //  NEW: manual vs email reminder
        public ReminderType Type { get; set; } = ReminderType.Manual;

        //   NEW: only used for email reminders
        public string? SourceProvider { get; set; }  // "Gmail"
        public string? SourceId { get; set; }        // Gmail MessageId
    }
}