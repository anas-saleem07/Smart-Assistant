using Microsoft.EntityFrameworkCore;
using SmartAssistant.Api.Data;
using SmartAssistant.Core.Entities;

namespace SmartAssistant.Api.Services
{
    public interface IReminderService
    {
        //  two different flows
        Task<Reminder> AddManualReminderAsync(Reminder reminder);
        Task<Reminder?> AddEmailReminderAsync(Reminder reminder);

        Task<IEnumerable<Reminder>> GetAllAsync();
        Task<bool> DeleteAsync(Guid id);

        Task<bool> ExistsEmailReminderAsync(string sourceProvider, string sourceId);
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
            //  Force MANUAL behavior (this is what you asked)
            reminder.Type = ReminderType.Manual;
            reminder.SourceProvider = null;
            reminder.SourceId = null;

            if (reminder.Id == Guid.Empty)
                reminder.Id = Guid.NewGuid();

            if (reminder.CreatedOn == default)
                reminder.CreatedOn = DateTime.UtcNow;

            // Manual reminder should not be completed on create
            reminder.Completed = false;

            _context.Reminder.Add(reminder);
            await _context.SaveChangesAsync();

            return reminder;
        }

        public async Task<Reminder?> AddEmailReminderAsync(Reminder reminder)
        {
            //  Force EMAIL behavior (this is what you asked)
            reminder.Type = ReminderType.Email;
            reminder.SourceProvider ??= "Gmail";

            if (string.IsNullOrWhiteSpace(reminder.SourceId))
                throw new ArgumentException("Email reminder requires SourceId (Gmail MessageId).");

            //  Dedupe: do not create if already exists
            var alreadyExists = await ExistsEmailReminderAsync(reminder.SourceProvider, reminder.SourceId);
            if (alreadyExists)
                return null;

            if (reminder.Id == Guid.Empty)
                reminder.Id = Guid.NewGuid();

            if (reminder.CreatedOn == default)
                reminder.CreatedOn = DateTime.UtcNow;

            reminder.Completed = false;

            _context.Reminder.Add(reminder);

            // In rare race condition, DB unique index may still throw; you can catch if you want.
            await _context.SaveChangesAsync();

            return reminder;
        }

        public async Task<bool> ExistsEmailReminderAsync(string sourceProvider, string sourceId)
        {
            if (string.IsNullOrWhiteSpace(sourceProvider) || string.IsNullOrWhiteSpace(sourceId))
                return false;

            return await _context.Reminder.AnyAsync(r =>
                r.Type == ReminderType.Email &&
                r.SourceProvider == sourceProvider &&
                r.SourceId == sourceId);
        }

        public async Task<IEnumerable<Reminder>> GetAllAsync()
        {
            return await _context.Reminder
                .OrderByDescending(r => r.CreatedOn)
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
    }
}