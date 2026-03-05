using System;
using System.Threading;
using System.Threading.Tasks;
using SmartAssistant.Core.Entities;

namespace SmartAssistant.Api.Services.Calendar
{
    public interface ICalendarService
    {
        Task<string> CreateEventAsync(Reminder reminder, ReminderAutomationSettings settings, CancellationToken ct);

        // New: Check if a time range is free on calendar
        Task<bool> IsFreeAsync(DateTimeOffset startUtc, DateTimeOffset endUtc, ReminderAutomationSettings settings, CancellationToken ct);

        // New: Find the next free slot within office hours
        Task<DateTimeOffset?> FindNextFreeSlotAsync(DateTimeOffset fromUtc, ReminderAutomationSettings settings, CancellationToken ct);
    }
}