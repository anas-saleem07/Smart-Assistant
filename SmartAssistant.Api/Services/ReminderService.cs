using Microsoft.EntityFrameworkCore;
using SmartAssistant.Api.Data;
using SmartAssistant.Core.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SmartAssistant.Api.Services
{
    public interface IReminderService
    {
        Task<Reminder> AddManualReminderAsync(Reminder reminder);
        Task<Reminder?> AddEmailReminderAsync(Reminder reminder);

        Task<IEnumerable<Reminder>> GetAllAsync();
        Task<bool> DeleteAsync(Guid id);

        Task<bool> ExistsEmailReminderAsync(string sourceProvider, string sourceId, string? calendarEventId = null);

        Task<(bool Success, string? ErrorMessage)> SetCompletedAsync(Guid id, bool completed);
    }

    public class ReminderService : IReminderService
    {
        private readonly ApplicationDbContext _context;

        public ReminderService(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<Reminder> AddManualReminderAsync(Reminder reminder)
        {
            reminder.Type = ReminderType.Manual;
            reminder.SourceProvider = null;
            reminder.SourceId = null;

            if (reminder.Id == Guid.Empty)
                reminder.Id = Guid.NewGuid();

            if (reminder.CreatedOn == default)
                reminder.CreatedOn = DateTime.UtcNow;

            reminder.Completed = false;

            // IMPORTANT:
            // ReminderController already converts UI local time to UTC before calling this method.
            // So do NOT convert again here.
            // Just normalize to UTC safely.
            reminder.ReminderTime = reminder.ReminderTime.ToUniversalTime();

            _context.Reminder.Add(reminder);
            await _context.SaveChangesAsync();

            return reminder;
        }

        public async Task<Reminder?> AddEmailReminderAsync(Reminder reminder)
        {
            reminder.Type = ReminderType.Email;
            reminder.SourceProvider ??= "Gmail";

            if (string.IsNullOrWhiteSpace(reminder.SourceId) && string.IsNullOrWhiteSpace(reminder.CalendarEventId))
                throw new ArgumentException("Email reminder requires SourceId or CalendarEventId.");

            var alreadyExists = await ExistsEmailReminderAsync(
                reminder.SourceProvider,
                reminder.SourceId ?? "",
                reminder.CalendarEventId);

            if (alreadyExists)
                return null;

            if (reminder.Id == Guid.Empty)
                reminder.Id = Guid.NewGuid();

            if (reminder.CreatedOn == default)
                reminder.CreatedOn = DateTime.UtcNow;

            reminder.Completed = false;

            // Email automation time is already expected in UTC or offset-aware time.
            // Normalize safely to UTC.
            reminder.ReminderTime = reminder.ReminderTime.ToUniversalTime();

            _context.Reminder.Add(reminder);
            await _context.SaveChangesAsync();

            return reminder;
        }

        public async Task<bool> ExistsEmailReminderAsync(string sourceProvider, string sourceId, string? calendarEventId = null)
        {
            if (string.IsNullOrWhiteSpace(sourceProvider))
                return false;

            return await _context.Reminder.AnyAsync(reminderItem =>
                reminderItem.Type == ReminderType.Email &&
                reminderItem.SourceProvider == sourceProvider &&
                (
                    (!string.IsNullOrWhiteSpace(sourceId) && reminderItem.SourceId == sourceId) ||
                    (!string.IsNullOrWhiteSpace(calendarEventId) && reminderItem.CalendarEventId == calendarEventId)
                ));
        }

        public async Task<IEnumerable<Reminder>> GetAllAsync()
        {
            return await _context.Reminder
                .OrderBy(reminderItem => reminderItem.ReminderTime)
                .ToListAsync();
        }

        public async Task<bool> DeleteAsync(Guid id)
        {
            var reminder = await _context.Reminder.FindAsync(id);
            if (reminder == null)
                return false;

            _context.Reminder.Remove(reminder);
            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<(bool Success, string? ErrorMessage)> SetCompletedAsync(Guid id, bool completed)
        {
            var reminder = await _context.Reminder.FirstOrDefaultAsync(reminderItem => reminderItem.Id == id);
            if (reminder == null)
                return (false, "Reminder not found.");

            // Rule:
            // Can only mark as completed after due time is reached.
            if (completed && reminder.ReminderTime > DateTimeOffset.UtcNow)
                return (false, "Reminder can only be marked completed when its due time is reached.");

            reminder.Completed = completed;
            await _context.SaveChangesAsync();

            return (true, null);
        }
    }
}