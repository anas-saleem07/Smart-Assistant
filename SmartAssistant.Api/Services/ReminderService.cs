using Microsoft.EntityFrameworkCore;
using SmartAssistant.Api.Data;
using SmartAssistant.Core.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SmartAssistant.Api.Services
{
    #region Interface
    public interface IReminderService
    {
        Task<Reminder> AddManualReminderAsync(Reminder reminder, string? accountEmail);
        Task<Reminder?> AddEmailReminderAsync(Reminder reminder);
        Task<bool> DeleteAsync(Guid id);
        Task<bool> ExistsEmailReminderAsync(string sourceProvider, string sourceId, string? calendarEventId = null);
        Task<IEnumerable<Reminder>> GetAllAsync();
        Task<IEnumerable<Reminder>> GetAllAsync(string? activeAccountEmail);
        Task<(bool Success, string? ErrorMessage)> SetCompletedAsync(Guid id, bool completed);
    }
    #endregion

    public class ReminderService : IReminderService
    {
        #region Fields
        private readonly ApplicationDbContext _context;
        #endregion

        #region Constructor
        public ReminderService(ApplicationDbContext context)
        {
            _context = context;
        }
        #endregion

        #region Public methods
        public async Task<Reminder> AddManualReminderAsync(Reminder reminder, string? accountEmail)
        {
            reminder.Type = ReminderType.Manual;
            reminder.SourceProvider = null;
            reminder.SourceId = null;
            reminder.AccountEmail = string.IsNullOrWhiteSpace(accountEmail) ? null : accountEmail.Trim();

            if (reminder.Id == Guid.Empty)
                reminder.Id = Guid.NewGuid();

            if (reminder.CreatedOn == default)
                reminder.CreatedOn = DateTime.UtcNow;

            reminder.Completed = false;
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
            reminder.ReminderTime = reminder.ReminderTime.ToUniversalTime();

            _context.Reminder.Add(reminder);
            await _context.SaveChangesAsync();

            return reminder;
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

        public async Task<IEnumerable<Reminder>> GetAllAsync(string? activeAccountEmail)
        {
            var query = _context.Reminder.AsQueryable();

            if (!string.IsNullOrWhiteSpace(activeAccountEmail))
            {
                query = query.Where(reminderItem => reminderItem.AccountEmail == activeAccountEmail);
            }

            return await query
                .OrderBy(reminderItem => reminderItem.ReminderTime)
                .ToListAsync();
        }

        public async Task<(bool Success, string? ErrorMessage)> SetCompletedAsync(Guid id, bool completed)
        {
            var reminder = await _context.Reminder.FirstOrDefaultAsync(reminderItem => reminderItem.Id == id);
            if (reminder == null)
                return (false, "Reminder not found.");

            if (completed && reminder.ReminderTime > DateTimeOffset.UtcNow)
                return (false, "Reminder can only be marked completed when its due time is reached.");

            reminder.Completed = completed;
            await _context.SaveChangesAsync();

            return (true, null);
        }
        #endregion
    }
}