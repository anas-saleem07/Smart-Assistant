using System.Reflection;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SmartAssistant.Api.Data;
using SmartAssistant.Api.Services;
using SmartAssistant.Api.Services.Automation;
using SmartAssistant.Api.Services.AutoReply;
using SmartAssistant.Api.Services.Calendar;
using SmartAssistant.Api.Services.Email;
using SmartAssistant.Core.Entities;

var checks = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new Exception("FAIL: " + name);
    Console.WriteLine("PASS: " + name);
    checks++;
}

var received = new DateTimeOffset(2026, 9, 27, 19, 41, 0, TimeSpan.FromHours(5));
EmailMessage Mail(string text, DateTimeOffset? arrival = null) =>
    new("Gmail", Guid.NewGuid().ToString(), "Project meeting and upcoming tasks", text,
        arrival ?? received, "Anas Saleem <anas@example.com>");
var settings = new ReminderAutomationSettings();
var parse = typeof(AutoReplyService).GetMethod("TryGetProposedUtcRange", BindingFlags.NonPublic | BindingFlags.Static)!;
(bool Ok, DateTimeOffset Start, DateTimeOffset End) Parse(string text)
{
    object?[] args = { Mail(text), settings, null, null, true };
    var ok = (bool)parse.Invoke(null, args)!;
    return (ok, (DateTimeOffset)args[2]!, (DateTimeOffset)args[3]!);
}
foreach (var text in new[] {
    "Meeting on Tuesday, September 29 at 10:00 AM",
    "Interview scheduled for September 29 at 10 AM",
    "Let's meet Tuesday at 10:00 AM" })
{
    var result = Parse(text);
    Check(result.Ok && result.Start == new DateTimeOffset(2026, 9, 29, 5, 0, 0, TimeSpan.Zero)
        && result.End - result.Start == TimeSpan.FromMinutes(30), text);
}
foreach (var text in new[] {
    "September 29, 2026 at 10:00 AM - 11:00 AM",
    "between 10:00 AM and 11:00 AM on September 29, 2026",
    "29 Sep 2026 10:00am - 11:00am",
    "Tuesday, September 29, 2026 - 10:00 AM - 11:00 AM",
    "Tue 29 Sep 2026 10:00am - 11:00am (GMT+5)" })
{
    var result = Parse(text);
    Check(result.Ok && result.End - result.Start == TimeSpan.FromHours(1), "Existing range: " + text);
}
Check(!Parse("September 31 at 10 AM").Ok, "Invalid date rejected");
Check(!Parse("September 29 at 25 AM").Ok, "Invalid time rejected");
Check(!Parse("September 29 at 10 AM - 9 AM").Ok, "Unsupported explicit range does not lose its end");
Check(!Parse("Please arrange a meeting").Ok, "Missing date/time rejected");
foreach (var sample in new[] {
    ("Project meeting on Tuesday, October 6 at 10:00 AM", 6, 10, 0, 30),
    ("Meeting Tuesday at 10 AM", 6, 10, 0, 30),
    ("Interview scheduled for October 6 at 2:30 PM", 6, 14, 30, 30),
    ("Can we meet tomorrow at 11:00 AM?", 6, 11, 0, 30),
    ("Let's meet Monday at 9 AM", 12, 9, 0, 30),
    ("October 6, 2026 at 10:00 AM", 6, 10, 0, 30),
    ("Tuesday, October 6, 2026 10:00 AM", 6, 10, 0, 30),
    ("Meeting from 10:00 AM to 10:30 AM on October 6", 6, 10, 0, 30),
    ("Meeting at 10:00 AM on October 6", 6, 10, 0, 30),
    ("Interview on October 6 from 11:00 AM to 11:30 AM", 6, 11, 0, 30) })
{
    object?[] sampleArgs = { Mail(sample.Item1, new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.FromHours(5))), settings, null, null, true };
    var ok = (bool)parse.Invoke(null, sampleArgs)!;
    Check(ok && (DateTimeOffset)sampleArgs[2]! == new DateTimeOffset(2026, 10, sample.Item2, sample.Item3, sample.Item4, 0, TimeSpan.FromHours(5))
        && (DateTimeOffset)sampleArgs[3]! - (DateTimeOffset)sampleArgs[2]! == TimeSpan.FromMinutes(sample.Item5), "Natural expression: " + sample.Item1);
}
object?[] rollover = { Mail("October 6 at 10 AM", new DateTimeOffset(2026, 10, 7, 0, 0, 0, TimeSpan.Zero)), settings, null, null, true };
Check((bool)parse.Invoke(null, rollover)! && ((DateTimeOffset)rollover[2]!).Year == 2027, "Omitted year resolves after receipt, not into the past");
var queryBuilder = typeof(GmailEmailClient).GetMethod("BuildScanQuery", BindingFlags.NonPublic | BindingFlags.Static)!;
var query = (string)queryBuilder.Invoke(null, new object?[] { "in:inbox newer_than:7d -category:social" })!;
Check(query.Contains("newer_than:14d") && !query.Contains("newer_than:7d") && query.Contains("-category:social"), "Persisted Gmail query widened, other filters retained");

async Task RunScenario(string name, string body, bool replyOn, int ageDays, bool processed, bool shouldCreate, bool shouldSkip = false)
{
    using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    db.ReminderAutomationSettings.Add(new ReminderAutomationSettings { Id = 1, AutoReplyEnabled = replyOn });
    var email = Mail(body, DateTimeOffset.UtcNow.AddDays(-ageDays));
    if (processed) db.EmailProcessed.Add(new EmailProcessed { Provider = email.Provider, MessageId = email.Id });
    await db.SaveChangesAsync();
    var client = new StubEmailClient(email);
    var reply = DispatchProxy.Create<IAutoReplyService, ReplyProbe>();
    var probe = (ReplyProbe)(object)reply;
    var automation = new ReminderAutomationService(db, new ReminderService(db), client, null!, reply);
    await automation.ScanAndCreateRemindersAsync(default);
    Check(await db.Reminder.AnyAsync() == shouldCreate, name + ": reminder result");
    Check(probe.ReplyCalls == (replyOn && !processed && ageDays <= 14 ? 1 : 0), name + ": reply routing");
    Check(client.Since < DateTimeOffset.UtcNow.AddDays(-13.99), name + ": fourteen-day cutoff");
    if (shouldCreate || shouldSkip)
    {
        var row = await db.EmailProcessed.SingleAsync();
        Check(row.Subject == email.Subject && row.From == email.From, name + ": metadata persisted");
        Check(!row.ReplyNeeded && !row.ReplyRequiresApproval, name + ": no reply/approval side effects");
        Check(shouldCreate ? row.ProcessingStatus == ProcessingStatuses.ReminderCreated && row.ReplyLastError == null
            : row.ProcessingStatus == ProcessingStatuses.Skipped && row.ReplyLastError!.Contains("date/time"), name + ": accurate outcome/reason");
        if (shouldCreate)
            Check((await db.Reminder.SingleAsync()).ReminderTime == row.ProposedStartUtc, name + ": parsed start used");
        var history = await new AutoReplyService(db, null!, client, null!).GetProcessedEmailHistoryAsync(default);
        var json = JsonSerializer.Serialize(history.Single());
        Check(history.Single().Subject == email.Subject && history.Single().SenderStatus == "Known"
            && json.Contains("SenderStatus") && json.Contains(email.Subject), name + ": real history response");
        await automation.ScanAndCreateRemindersAsync(default);
        Check(await db.EmailProcessed.CountAsync() == 1 && await db.Reminder.CountAsync() == (shouldCreate ? 1 : 0), name + ": second scan deduplicated");
    }
}
await RunScenario("B start only", "October 2, 2026 at 10 AM", false, 0, false, true);
await RunScenario("C range", "October 2, 2026 at 10:00 AM - 11:00 AM", false, 0, false, true);
await RunScenario("A auto reply on", "October 2, 2026 at 10:00 AM - 11:00 AM", true, 0, false, false);
await RunScenario("D missing time", "Please arrange a meeting", false, 0, false, false, true);
await RunScenario("E ten-day unprocessed", "October 2, 2026 at 10 AM", false, 10, false, true);
await RunScenario("F ten-day processed", "October 2, 2026 at 10 AM", false, 10, true, false);
await RunScenario("G fifteen-day email", "October 2, 2026 at 10 AM", false, 15, false, false);
Check(new ProcessedEmailHistoryDto { From = "(unknown)" }.SenderStatus == "Unknown", "Unknown sender retained");
Check(new ProcessedEmailHistoryDto { From = "HR" }.SenderStatus == "Unknown", "Display name alone is not blindly Known");

foreach (var qualifies in new[] { false, true })
{
    using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    db.ReminderAutomationSettings.Add(new ReminderAutomationSettings());
    await db.SaveChangesAsync();
    var email = Mail("No meeting date is specified", DateTimeOffset.UtcNow) with
    { Subject = qualifies ? "Urgent action required" : "Monthly newsletter", Snippet = "Please review the document." };
    var reply = DispatchProxy.Create<IAutoReplyService, ReplyProbe>();
    await new ReminderAutomationService(db, new ReminderService(db), new StubEmailClient(email), null!, reply).ScanAndCreateRemindersAsync(default);
    var row = await db.EmailProcessed.SingleAsync();
    Check(qualifies ? await db.Reminder.AnyAsync() && row.ReplyLastError == null
        : !await db.Reminder.AnyAsync() && row.ReplyLastError == "Message did not qualify for reminder creation.",
        qualifies ? "Non-scheduling default-delay reminder preserved" : "Nonmatching email has a truthful reason");
}

using (var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
    .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options))
{
    db.ReminderAutomationSettings.Add(new ReminderAutomationSettings());
    await db.SaveChangesAsync();
    var reminders = DispatchProxy.Create<IReminderService, ReminderFailureProbe>();
    ((ReminderFailureProbe)(object)reminders).Db = db;
    var automation = new ReminderAutomationService(db, reminders,
        new StubEmailClient(Mail("October 2, 2026 at 10 AM", DateTimeOffset.UtcNow)), null!,
        DispatchProxy.Create<IAutoReplyService, ReplyProbe>());
    try { await automation.ScanAndCreateRemindersAsync(default); throw new Exception("Expected failure"); }
    catch (InvalidOperationException ex) when (ex.Message == "Simulated reminder persistence failure") { }
    var row = await db.EmailProcessed.SingleAsync();
    Check(row.ProcessingStatus == ProcessingStatuses.Error && row.ReplyLastError!.StartsWith("Reminder creation failed"), "Persistence failure has an accurate safe reason");
    Check(((ReminderFailureProbe)(object)reminders).NoEarlyProcessedRow, "No terminal row before reminder creation attempt");
    Check((await db.ReminderAutomationSettings.SingleAsync()).LastRunError == "Simulated reminder persistence failure", "Original failure retained in scan diagnostics");
}

foreach (var startOnly in new[] { false, true })
{
    using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    db.ReminderAutomationSettings.Add(new ReminderAutomationSettings { AutoReplyEnabled = true, OfficeStartHour = 9, OfficeEndHour = 17 });
    await db.SaveChangesAsync();
    var client = new StubEmailClient(Mail(startOnly ? "Project meeting on Tuesday, October 6 at 10:00 AM" : "October 6, 2026 at 10:00 AM - 11:00 AM", new DateTimeOffset(2026, 10, 5, 7, 0, 0, TimeSpan.Zero))) { AllowMockReply = true };
    var calendar = DispatchProxy.Create<ICalendarService, CalendarProbe>();
    var service = new AutoReplyService(db, null!, client, calendar);
    await new ReminderAutomationService(db, new ReminderService(db), client, calendar, service).ScanAndCreateRemindersAsync(default);
    var row = await db.EmailProcessed.SingleAsync();
    Check(await db.Reminder.CountAsync() == 1 && row.ProcessingStatus == ProcessingStatuses.AutoAccepted,
        "Real Auto Reply service accepts " + (startOnly ? "start-only time" : "existing range"));
    Check(row.ProposedStartUtc == new DateTimeOffset(2026, 10, 6, 5, 0, 0, TimeSpan.Zero) && (await db.Reminder.SingleAsync()).ReminderTime == row.ProposedStartUtc, "Exact October 6 10AM PKT survives parser, office-hours, conflicts and reminder mapping");
    Check(client.ReplyCalls == 1 && row.Replied && !row.ReplyNeeded, "Existing reply workflow completes using a mock");
    Check((await db.Reminder.SingleAsync()).CalendarEventId == "mock-event", "Existing Calendar workflow completes using a mock");
    Check((await db.ReminderAutomationSettings.SingleAsync()).AiCallsToday == 1, "Existing quota accounting preserved");
}
checks += await HistoryApprovalChecks.RunAsync();
Console.WriteLine($"All {checks} checks passed. No Google services or production database used.");

public class ReplyProbe : DispatchProxy
{
    public int ReplyCalls;
    protected override object? Invoke(MethodInfo? method, object?[]? args)
    {
        if (method!.Name == nameof(IAutoReplyService.TryAutoReplyAsync)) ReplyCalls++;
        return Task.FromResult(false);
    }
}
public sealed class StubEmailClient(EmailMessage email) : IEmailClient
{
    public DateTimeOffset Since;
    public bool AllowMockReply;
    public int ReplyCalls;
    public Task<IReadOnlyList<EmailMessage>> GetImportantEmailsAsync(DateTimeOffset sinceUtc, CancellationToken ct, string? gmailQuery = null)
    {
        Since = sinceUtc;
        return Task.FromResult<IReadOnlyList<EmailMessage>>(new[] { email });
    }
    public Task<EmailMessage?> GetEmailByIdAsync(string messageId, CancellationToken ct) => Task.FromResult<EmailMessage?>(email);
    public Task ReplyAsync(string messageId, string body, CancellationToken ct)
    {
        if (!AllowMockReply) throw new Exception("Unexpected reply in reminder-only checks.");
        ReplyCalls++;
        return Task.CompletedTask;
    }
}
public class CalendarProbe : DispatchProxy
{
    protected override object? Invoke(MethodInfo? method, object?[]? args) => method!.Name switch
    {
        nameof(ICalendarService.IsFreeAsync) => Task.FromResult(true),
        nameof(ICalendarService.CreateEventAsync) => Task.FromResult("mock-event"),
        _ => throw new Exception("Unexpected Calendar operation: " + method.Name)
    };
}
public class ReminderFailureProbe : DispatchProxy
{
    public ApplicationDbContext Db = null!;
    public bool NoEarlyProcessedRow;
    protected override object? Invoke(MethodInfo? method, object?[]? args)
    {
        if (method!.Name == nameof(IReminderService.ExistsEmailReminderAsync)) return Task.FromResult(false);
        if (method.Name == nameof(IReminderService.AddEmailReminderAsync))
        {
            NoEarlyProcessedRow = !Db.EmailProcessed.Any();
            return Task.FromException<Reminder?>(new InvalidOperationException("Simulated reminder persistence failure"));
        }
        throw new Exception("Unexpected reminder operation");
    }
}
