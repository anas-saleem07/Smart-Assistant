using SmartAssistant.Api.Data;
using SmartAssistant.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace SmartAssistant.Api.Services
{

    public interface IReminderService
    {
        Task<Reminder> AddReminderAsync(Reminder reminder);
        Task<IEnumerable<Reminder>> GetAllAsync();
        Task<bool> DeleteAsync(int id);
    }
    public class ReminderService : IReminderService
    {
        private readonly ApplicationDbContext _context;

        public ReminderService(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<Reminder> AddReminderAsync(Reminder reminder)
        {
            _context.Reminder.Add(reminder);
            await _context.SaveChangesAsync();
            return reminder;
        }

        public async Task<IEnumerable<Reminder>> GetAllAsync()
        {
            return await _context.Reminder.ToListAsync();
        }

        public async Task<bool> DeleteAsync(int id)
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
