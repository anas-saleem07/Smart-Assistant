public sealed class ReminderViewModel
{
    public Guid Id { get; set; }
    public string? Title { get; set; }
    public string? Description { get; set; }
    public DateTimeOffset ReminderTime { get; set; }
    public string ReminderTimeLocalText { get; set; } = "";
    public bool Completed { get; set; }
    public DateTime CreatedOn { get; set; }
}