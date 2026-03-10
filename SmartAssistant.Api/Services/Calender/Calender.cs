using System;
using System.Threading;
using System.Threading.Tasks;
using SmartAssistant.Core.Entities;

namespace SmartAssistant.Api.Services.Calendar
{
    public interface ICalendarService
    {
        Task<string> CreateEventAsync(Reminder reminder, ReminderAutomationSettings settings, CancellationToken ct);

        Task<bool> IsFreeAsync(DateTimeOffset startUtc, DateTimeOffset endUtc, ReminderAutomationSettings settings, CancellationToken ct);

        Task<DateTimeOffset?> FindNextFreeSlotAsync(DateTimeOffset fromUtc, ReminderAutomationSettings settings, CancellationToken ct);

        // ADD
        Task<DateTimeOffset?> FindNextFreeSlotOnSameDayAsync(DateTimeOffset preferredStartUtc, ReminderAutomationSettings settings, CancellationToken ct);

        // ADD
        Task<bool> AcceptInviteAsync(string calendarEventId, ReminderAutomationSettings settings, CancellationToken ct);

        Task<CalendarApprovalEventResult?> CreateApprovalSuggestionEventAsync(DateTimeOffset startUtc,DateTimeOffset endUtc,string title,string description,ReminderAutomationSettings settings,CancellationToken ct);
        Task<CalendarEventSnapshot?> GetEventSnapshotAsync(string calendarEventId, ReminderAutomationSettings settings, CancellationToken ct);
    }
    public sealed class CalendarApprovalEventResult
    {
        // Public link for opening the event in Google Calendar UI
        public string EventHtmlLink { get; set; } = "";

        // Google event id
        public string EventId { get; set; } = "";

        // Useful for UI display/debug
        public DateTimeOffset StartUtc { get; set; }
        public DateTimeOffset EndUtc { get; set; }
    }
    
    public sealed class CalendarEventSnapshot
    {
        public string EventId { get; set; } = "";
        public DateTimeOffset? StartUtc { get; set; }
        public DateTimeOffset? EndUtc { get; set; }
        public string HtmlLink { get; set; } = "";
    }
    
}