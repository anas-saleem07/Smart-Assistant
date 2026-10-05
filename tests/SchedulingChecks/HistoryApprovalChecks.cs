using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SmartAssistant.Api.Controllers;
using SmartAssistant.Api.Data;
using SmartAssistant.Api.Services.AutoReply;
using SmartAssistant.Api.Services.Calendar;
using SmartAssistant.Api.Services.Email;
using SmartAssistant.Core.Entities;
using SchedulingChecks.Components;

public static class HistoryApprovalChecks
{
    public static async Task<int> RunAsync()
    {
        var count = 0;
        void Check(bool condition, string name)
        {
            if (!condition) throw new Exception("FAIL: " + name);
            Console.WriteLine("PASS: " + name);
            count++;
        }

        using (var fixture = new Fixture())
        {
            var legacy = fixture.AddRow(null);
            legacy.ReplyRequiresApproval = true;
            legacy.ReplyNeeded = true;
            legacy.Subject = null;
            legacy.From = null;
            legacy.ReplyLastError = "Original proposed time is missing. Please use suggested-slot flow.";
            fixture.Email.Messages[legacy.MessageId] = new EmailMessage("Gmail", legacy.MessageId,
                "Project meeting and upcoming tasks", "Meeting text", DateTimeOffset.UtcNow, "Anas <anas@example.com>");
            var accepted = fixture.AddRow(ProcessingStatuses.Confirmed);
            accepted.Replied = true;
            accepted.ReplyLastError = "Original slot approved and confirmed.";
            var declined = fixture.AddRow(ProcessingStatuses.RejectedBySender);
            var rescheduled = fixture.AddRow(ProcessingStatuses.ReschedulePending);
            var failed = fixture.AddRow(ProcessingStatuses.Error);
            failed.ReplyLastError = "Reply failed: mail service unavailable";
            var calendarFailed = fixture.AddRow(ProcessingStatuses.AutoAccepted);
            fixture.Db.Reminder.Add(new Reminder { Id = Guid.NewGuid(), SourceProvider = "Gmail", SourceId = calendarFailed.MessageId,
                AccountEmail = Fixture.Account, CalendarSyncError = "calendar unavailable", Type = ReminderType.Email });
            var pendingWithoutReason = fixture.AddRow(ProcessingStatuses.ApprovalPending);
            pendingWithoutReason.ReplyLastError = "Approval pending.";
            await fixture.Db.SaveChangesAsync();

            var response = await fixture.Controller.GetHistory(default);
            var history = (List<ProcessedEmailHistoryDto>)((OkObjectResult)response.Result!).Value!;
            Check(history.Single(x => x.Id == legacy.Id).Subject == "Project meeting and upcoming tasks" && legacy.Subject == "Project meeting and upcoming tasks", "Legacy subject restored from Gmail metadata and persisted");
            Check(history.Single(x => x.Id == legacy.Id).ProcessingStatus == ProcessingStatuses.ApprovalPending, "Missing workflow status recovered from approval flags");
            Check(history.Single(x => x.Id == legacy.Id).Details.Contains("Original proposed time is missing"), "Actual pending reason survives API mapping");
            Check(history.Single(x => x.Id == accepted.Id).Details == "", "Successful acceptance notice is not an error");
            Check(history.Single(x => x.Id == calendarFailed.Id).Details == "Calendar creation failed: calendar unavailable", "Calendar failure remains visible on an accepted record");
            Check(history.Single(x => x.Id == failed.Id).Details == failed.ReplyLastError, "Real reply error preserved");
            Check(history.Single(x => x.Id == pendingWithoutReason.Id).Details == "", "Workflow-only text omitted from Error/Reason");
            var reads = fixture.Email.MetadataReads;
            await fixture.Service.GetProcessedEmailHistoryAsync(default);
            Check(fixture.Email.MetadataReads == reads && fixture.Email.Replies == 0, "Metadata repair is cached without reprocessing or replying");

            await using var ui = new UiSession(fixture);
            var html = await ui.RenderHistoryAsync();
            Check(html.Contains("Project meeting and upcoming tasks") && !html.Contains("Processed scheduling email"), "Actual history Razor renders original subject");
            Check(html.Contains("Approval Pending") && !html.Contains("Unknown") && !html.Contains(">Known<"), "Workflow status rendered once without sender classification");
            Check(html.Contains(">Accepted<") && html.Contains(">Declined<") && html.Contains("Waiting Reschedule"), "Accepted, declined and existing reschedule labels rendered");
            Check(html.Contains("Calendar creation failed") && html.Contains("Reply failed"), "History Razor does not hide real failures");
        }

        foreach (var scenario in new[] { "original", "suggested", "computed-suggestion", "neither", "suggest-button", "original-draft", "reschedule-button" })
        {
            using var fixture = new Fixture();
            var row = fixture.AddRow(ProcessingStatuses.ApprovalPending);
            row.ReplyNeeded = true;
            row.ReplyRequiresApproval = true;
            if (scenario == "suggest-button") row.ReplyLastError = "Original proposed time is missing.";
            if (scenario == "original") row.ProposedStartUtc = fixture.Calendar.Suggestion;
            if (scenario == "original-draft") row.ProposedStartUtc = fixture.Calendar.Suggestion;
            if (scenario is "suggested" or "reschedule-button") row.SuggestedStartUtc = fixture.Calendar.Suggestion;
            if (scenario == "neither") fixture.Calendar.Suggestion = null;
            await fixture.Db.SaveChangesAsync();
            await using var ui = new UiSession(fixture);
            var html = await ui.RenderPendingAsync();
            var component = (AutoReplyApprovalBar)ui.Activator.Components.Single(x => x is AutoReplyApprovalBar);
            var pending = (List<AutoReplyApprovalBar.PendingAutoReplyViewModel>)typeof(AutoReplyApprovalBar)
                .GetField("pendingItems", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(component)!;
            var item = pending.Single();
            if (scenario == "computed-suggestion")
                Check(row.SuggestedStartUtc == item.SuggestedStartUtc && row.SuggestedEndUtc == item.SuggestedEndUtc, "Displayed suggestion persisted for the approval action");
            var method = scenario is "suggest-button" or "original-draft" ? "OpenGoogleCalendarForSuggestedSlot"
                : scenario == "reschedule-button" ? "SendSuggestedSlot" : "ApproveAvailableSlot";
            await ui.Renderer.Dispatcher.InvokeAsync(async () =>
                await (Task)typeof(AutoReplyApprovalBar).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(component, new object[] { item })!);
            if (scenario is "neither" or "suggested" or "computed-suggestion")
            {
                Check(fixture.Handler.Posts.Count == 0 && html.Contains("disabled"), scenario + ": no original slot: Yes is disabled and sends no invalid request");
            }
            else if (scenario is "suggest-button" or "original-draft")
            {
                Check(fixture.Handler.Posts.Single().Contains("open-suggested-calendar") && row.SuggestedCalendarEventId == "draft-event", "Existing Suggest time slot action creates draft through existing endpoint");
                Check(fixture.Email.Replies == 0, "Suggest time slot does not send a reply");
                if (scenario == "suggest-button")
                    Check(row.ReplyLastError == "Original proposed time is missing.", "Saving a draft does not overwrite the actual missing-time reason");
                var refreshed = (List<AutoReplyApprovalBar.PendingAutoReplyViewModel>)typeof(AutoReplyApprovalBar)
                    .GetField("pendingItems", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(component)!;
                Check(refreshed.Single().HasSuggestedDraft && !refreshed.Single().CanApproveOriginal && !await fixture.Db.Reminder.AnyAsync(), "Draft immediately switches controls without finalizing a reminder");
            }
            else
            {
                var original = scenario != "reschedule-button";
                Check(fixture.Handler.Posts.Single().Contains(original ? "approve-same-slot" : "approve-suggested-slot"), scenario + ": actual Yes/button handler chooses correct existing endpoint");
                Check(fixture.Handler.LastPostStatus == HttpStatusCode.OK, scenario + ": existing controller accepts action without HTTP 400");
                Check(row.ProcessingStatus == (original ? ProcessingStatuses.Confirmed : ProcessingStatuses.WaitingSenderConfirmation), scenario + ": existing acceptance/confirmation semantics preserved");
                Check(fixture.Email.Replies == 1 && await fixture.Db.Reminder.CountAsync() == (original ? 1 : 0), scenario + ": reminder/reply effects preserved");
                var refreshed = (List<AutoReplyApprovalBar.PendingAutoReplyViewModel>)typeof(AutoReplyApprovalBar)
                    .GetField("pendingItems", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(component)!;
                Check(refreshed.Count == 0, scenario + ": successful action immediately removes approval card");
                if (!original) Check(row.WaitingForExternalConfirmation && row.SuggestedStartUtc.HasValue, "Sent suggestion retained while waiting for sender confirmation");
            }
        }

        using (var fixture = new Fixture())
        {
            var row = fixture.AddRow(ProcessingStatuses.ApprovalPending);
            row.ReplyNeeded = row.ReplyRequiresApproval = true;
            row.Subject = "Reschedule project meeting";
            row.ReplyLastError = "Original proposed time is missing. Please use suggested-slot flow.";
            row.SuggestedStartUtc = fixture.Calendar.Suggestion;
            await fixture.Db.SaveChangesAsync();
            await fixture.Service.TryAutoReplyAsync(new EmailMessage("Gmail", row.MessageId, row.Subject,
                "Could we reschedule to October 5 at 10 AM", DateTimeOffset.UtcNow, row.From!),
                await fixture.Db.ReminderAutomationSettings.SingleAsync(), default);
            Check(row.ProposedStartUtc.HasValue && row.ReplyLastError == null && row.SuggestedStartUtc == fixture.Calendar.Suggestion && row.ProcessingStatus == ProcessingStatuses.ApprovalPending, "Pending repair restores original start without reprocessing approval or suggestion");
            row.ReplyLastError = "Existing approval failure detail";
            fixture.Email.FailReplies = true;
            try { await fixture.Service.ApprovePendingReplyAsync(row.Id, true, default); }
            catch (InvalidOperationException ex) when (ex.Message == "simulated Gmail failure") { }
            var history = await fixture.Service.GetProcessedEmailHistoryAsync(default);
            Check(history.Single().Details.Contains("Reply failed: simulated Gmail failure") && history.Single().Details.Contains("Existing approval failure detail"), "Reply failure persists without discarding existing reason");
            Check(row.ProcessingStatus == ProcessingStatuses.Error && !row.Replied, "Failed send is not marked accepted or replied");
        }
        foreach (var scenario in new[] { "start", "range", "closing-start", "after-hours", "reminder-conflict", "calendar-conflict", "conjunction", "unparseable" })
        {
            using var fixture = new Fixture();
            var settings = await fixture.Db.ReminderAutomationSettings.SingleAsync();
            settings.AllowAutoReplyAfterOfficeHours = true; // Explicit business rule still requires approval.
            var text = scenario switch
            {
                "range" => "Meeting Tuesday at 10:00 AM - 11:00 AM on October 6, 2026",
                "closing-start" => "Meeting Tuesday at 5:45 PM",
                "after-hours" => "Meeting Tuesday at 8:00 PM",
                "conjunction" => "Meeting Tuesday at 10:00 AM and please bring your notes",
                "unparseable" => "Please arrange a project meeting sometime",
                _ => "Meeting Tuesday at 10:00 AM"
            };
            if (scenario == "range") text = "Meeting October 6, 2026 at 10:00 AM - 11:00 AM";
            if (scenario == "calendar-conflict") fixture.Calendar.Free = false;
            if (scenario == "reminder-conflict") fixture.Db.Reminder.Add(new Reminder { Id = Guid.NewGuid(), Type = ReminderType.Manual,
                ReminderTime = new DateTimeOffset(2026, 10, 6, 5, 0, 0, TimeSpan.Zero), AccountEmail = Fixture.Account });
            await fixture.Db.SaveChangesAsync();
            var email = new EmailMessage("Gmail", Guid.NewGuid().ToString(), "Project meeting", text,
                new DateTimeOffset(2026, 10, 5, 7, 0, 0, TimeSpan.Zero), "Anas <anas@example.com>");
            await fixture.Service.TryAutoReplyAsync(email, settings, default);
            var row = await fixture.Db.EmailProcessed.SingleAsync();
            var pending = scenario is "after-hours" or "reminder-conflict" or "calendar-conflict" or "unparseable";
            Check(row.ProcessingStatus == (pending ? ProcessingStatuses.ApprovalPending : ProcessingStatuses.AutoAccepted), scenario + ": correct automatic branch");
            Check(await fixture.Db.Reminder.CountAsync(x => x.Type == ReminderType.Email) == (pending ? 0 : 1), scenario + ": correct reminder effect");
            Check(fixture.Email.Replies == (pending ? 0 : 1), scenario + ": no automatic reply while approval required");
            if (scenario == "unparseable")
                Check(!row.ProposedStartUtc.HasValue && row.ReplyLastError!.Contains("could not be parsed"), "Unparseable request retains meaningful reason without invented original time");
            else if (pending)
            {
                var start = row.ProposedStartUtc;
                Check(await fixture.Service.ApprovePendingReplyAsync(row.Id, false, default), scenario + ": Yes explicitly approves despite the original gate");
                Check((await fixture.Db.Reminder.SingleAsync(x => x.Type == ReminderType.Email)).ReminderTime == start && row.ProcessingStatus == ProcessingStatuses.Confirmed,
                    scenario + ": Yes confirms the same requested start");
            }
        }
        using (var fixture = new Fixture())
        {
            var row = fixture.AddRow(ProcessingStatuses.ApprovalPending);
            row.ReplyNeeded = row.ReplyRequiresApproval = true;
            row.ProposedStartUtc = fixture.Calendar.Suggestion;
            await fixture.Db.SaveChangesAsync();
            Check(await fixture.Service.RejectPendingReplyAsync(row.Id, default), "Reject uses existing reschedule-request service");
            Check(!await fixture.Db.Reminder.AnyAsync() && fixture.Email.Replies == 1 && row.ProcessingStatus == ProcessingStatuses.ReschedulePending,
                "Reject requests rescheduling without creating a reminder");
        }
        using (var fixture = new Fixture())
        {
            await using var ui = new UiSession(fixture);
            await ui.RenderHistoryAsync();
            var component = (ProcessedEmailHistory)ui.Activator.Components.Single(x => x is ProcessedEmailHistory);
            var refresh = (ForegroundRefresh)ui.Activator.Components.Single(x => x is ForegroundRefresh);
            var row = fixture.AddRow(ProcessingStatuses.AutoAccepted);
            row.Subject = "Arrived while screen stayed open";
            await fixture.Db.SaveChangesAsync();
            SmartAssistant.App.ForegroundState.IsActive = false;
            var tick = typeof(ForegroundRefresh).GetMethod("RefreshIfActiveAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
            await ui.Renderer.Dispatcher.InvokeAsync(async () => await (Task)tick.Invoke(refresh, null)!);
            var field = typeof(ProcessedEmailHistory).GetField("items", BindingFlags.Instance | BindingFlags.NonPublic)!;
            Check(((List<ProcessedEmailHistory.ProcessedEmailHistoryDto>)field.GetValue(component)!).Count == 0, "Background refresh does not load state");
            SmartAssistant.App.ForegroundState.IsActive = true;
            await Task.Delay(TimeSpan.FromSeconds(21));
            await ui.Renderer.Dispatcher.InvokeAsync(() => Check(((List<ProcessedEmailHistory.ProcessedEmailHistoryDto>)field.GetValue(component)!).Single().Subject == row.Subject,
                "Actual 20-second timer refreshes mounted history without reopening"));
            Check(fixture.Handler.Posts.Count == 0 && fixture.Email.Replies == 0 && await fixture.Db.EmailProcessed.CountAsync() == 1, "Foreground polling only reads state without duplicate processing");
            refresh.Dispose();
            Check(refresh.Token.IsCancellationRequested, "Disposal cancels timer and linked requests");
        }
        return count;
    }

    private sealed class Fixture : IDisposable
    {
        public const string Account = "owner@example.com";
        public ApplicationDbContext Db { get; } = new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        public MetadataEmailClient Email { get; } = new();
        public ApprovalCalendarProbe Calendar { get; }
        public AutoReplyService Service { get; }
        public AutoReplyController Controller { get; }
        public ApiHandler Handler { get; }
        public Fixture()
        {
            var calendar = DispatchProxy.Create<ICalendarService, ApprovalCalendarProbe>();
            Calendar = (ApprovalCalendarProbe)(object)calendar;
            Service = new AutoReplyService(Db, null!, Email, calendar);
            Controller = new AutoReplyController(Service);
            Handler = new ApiHandler(this);
            Db.EmailOAuthAccounts.Add(new EmailOAuthAccount { Email = Account, RefreshToken = "test-only", Active = true });
            Db.ReminderAutomationSettings.Add(new ReminderAutomationSettings { AutoReplyEnabled = true });
            Db.SaveChanges();
        }
        public EmailProcessed AddRow(string? status)
        {
            var row = new EmailProcessed { Provider = "Gmail", MessageId = Guid.NewGuid().ToString(), AccountEmail = Account,
                Subject = "Project meeting and upcoming tasks", From = "Anas <anas@example.com>", ProcessingStatus = status };
            Db.EmailProcessed.Add(row);
            return row;
        }
        public void Dispose() => Db.Dispose();
    }
    private sealed class MetadataEmailClient : IEmailClient
    {
        public Dictionary<string, EmailMessage> Messages { get; } = new();
        public int MetadataReads, Replies;
        public bool FailReplies;
        public Task<EmailMessage?> GetEmailByIdAsync(string id, CancellationToken ct)
        { MetadataReads++; return Task.FromResult(Messages.GetValueOrDefault(id)); }
        public Task ReplyAsync(string id, string body, CancellationToken ct)
        { if (FailReplies) throw new InvalidOperationException("simulated Gmail failure"); Replies++; return Task.CompletedTask; }
        public Task<IReadOnlyList<EmailMessage>> GetImportantEmailsAsync(DateTimeOffset since, CancellationToken ct, string? query = null) => throw new Exception("History must not scan mail");
    }
    public class ApprovalCalendarProbe : DispatchProxy
    {
        public bool Free = true;
        public DateTimeOffset? Suggestion = new DateTimeOffset(2026, 10, 5, 4, 0, 0, TimeSpan.Zero);
        protected override object? Invoke(MethodInfo? method, object?[]? args) => method!.Name switch
        {
            nameof(ICalendarService.FindNextFreeSlotOnSameDayAsync) or nameof(ICalendarService.FindNextFreeSlotAsync) => Task.FromResult(Suggestion),
            nameof(ICalendarService.IsFreeAsync) => Task.FromResult(Free),
            nameof(ICalendarService.DeleteEventAsync) => Task.FromResult(true),
            nameof(ICalendarService.CreateEventAsync) => Task.FromResult("confirmed-event"),
            nameof(ICalendarService.CreateApprovalSuggestionEventAsync) => Task.FromResult<CalendarApprovalEventResult?>(new() { EventId = "draft-event", EventHtmlLink = "https://calendar.example.test/draft" }),
            nameof(ICalendarService.GetEventSnapshotAsync) => Task.FromResult<CalendarEventSnapshot?>(null),
            _ => throw new Exception("Unexpected calendar call: " + method.Name)
        };
    }
    private sealed class ApiHandler(Fixture fixture) : HttpMessageHandler
    {
        public List<string> Posts { get; } = new();
        public HttpStatusCode LastPostStatus;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            object? payload;
            var status = HttpStatusCode.OK;
            if (request.Method == HttpMethod.Get)
            {
                payload = path.EndsWith("/status") ? new { IsConnected = true }
                    : path.EndsWith("/history") ? ((OkObjectResult)(await fixture.Controller.GetHistory(ct)).Result!).Value
                    : ((OkObjectResult)(await fixture.Controller.GetPending(ct)).Result!).Value;
            }
            else
            {
                Posts.Add(path);
                var id = long.Parse(path.Split('/').Last());
                var action = path.Contains("approve-same-slot") ? await fixture.Controller.ApproveSameSlot(id, ct)
                    : path.Contains("approve-suggested-slot") ? await fixture.Controller.ApproveSuggestedSlot(id, ct)
                    : (await fixture.Controller.OpenSuggestedCalendar(id, ct)).Result!;
                var result = (ObjectResult)action;
                payload = result.Value;
                status = (HttpStatusCode)(result.StatusCode ?? 200);
                LastPostStatus = status;
            }
            return new HttpResponseMessage(status) { Content = JsonContent.Create(payload, payload?.GetType() ?? typeof(object)) };
        }
    }
    private sealed class CapturingActivator : IComponentActivator
    {
        public List<IComponent> Components { get; } = new();
        public IComponent CreateInstance(Type type)
        { var component = (IComponent)Activator.CreateInstance(type)!; Components.Add(component); return component; }
    }
    private sealed class TestNavigation : NavigationManager
    {
        public TestNavigation() => Initialize("http://test/", "http://test/");
        protected override void NavigateToCore(string uri, bool forceLoad) { }
    }
    private sealed class UiSession : IAsyncDisposable
    {
        public CapturingActivator Activator { get; } = new();
        private readonly ServiceProvider services;
        public HtmlRenderer Renderer { get; }
        public UiSession(Fixture fixture)
        {
            services = new ServiceCollection().AddLogging()
                .AddSingleton<IComponentActivator>(Activator)
                .AddSingleton<NavigationManager>(new TestNavigation())
                .AddSingleton(new HttpClient(fixture.Handler) { BaseAddress = new Uri("http://test/") })
                .BuildServiceProvider();
            Renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        }
        public Task<string> RenderHistoryAsync() => Renderer.Dispatcher.InvokeAsync(async () =>
            (await Renderer.RenderComponentAsync<ProcessedEmailHistory>()).ToHtmlString());
        public Task<string> RenderPendingAsync() => Renderer.Dispatcher.InvokeAsync(async () =>
            (await Renderer.RenderComponentAsync<AutoReplyApprovalBar>()).ToHtmlString());
        public async ValueTask DisposeAsync() { await Renderer.DisposeAsync(); await services.DisposeAsync(); }
    }
}
