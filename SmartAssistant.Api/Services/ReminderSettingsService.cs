using Microsoft.EntityFrameworkCore;
using SmartAssistant.Api.Data;
using SmartAssistant.Api.Models;
using SmartAssistant.Core.Entities;

namespace SmartAssistant.Api.Services.Settings
{
    public interface IReminderSettingService
    {
        Task<ReminderSettingsViewModel> GetAsync(CancellationToken cancellationToken);
        Task<ReminderSettingsViewModel> UpdateAsync(ReminderSettingsViewModel model, CancellationToken cancellationToken);
    }
    public class ReminderSettingsService : IReminderSettingService
    {
        private readonly ApplicationDbContext _dbContext;

        public ReminderSettingsService(ApplicationDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public async Task<ReminderSettingsViewModel> GetAsync(CancellationToken cancellationToken)
        {
            var settings = await GetOrCreateSettingsAsync(cancellationToken);
            return MapToViewModel(settings);
        }

        public async Task<ReminderSettingsViewModel> UpdateAsync(
            ReminderSettingsViewModel model,
            CancellationToken cancellationToken)
        {
            Validate(model);

            var settings = await GetOrCreateSettingsAsync(cancellationToken);

            // Update only the fields that user is allowed to edit
            settings.Enabled = model.Enabled;
            settings.ScanIntervalMinutes = model.ScanIntervalMinutes;
            settings.DefaultReminderAfterMinutes = model.DefaultReminderAfterMinutes;

            settings.KeywordsCsv = (model.KeywordsCsv ?? string.Empty).Trim();
            settings.GmailQuery = (model.GmailQuery ?? string.Empty).Trim();

            settings.AutoReplyEnabled = model.AutoReplyEnabled;
            settings.AutoReplyKeywordsCsv = (model.AutoReplyKeywordsCsv ?? string.Empty).Trim();

            settings.AiDailyLimit = model.AiDailyLimit;

            settings.OfficeStartHour = model.OfficeStartHour;
            settings.OfficeEndHour = model.OfficeEndHour;
            settings.SlotMinutes = model.SlotMinutes;
            settings.TimezoneId = string.IsNullOrWhiteSpace(model.TimezoneId)
                ? "Asia/Karachi"
                : model.TimezoneId.Trim();

            settings.CalendarEnabled = model.CalendarEnabled;
            settings.CalendarId = string.IsNullOrWhiteSpace(model.CalendarId)
                ? "primary"
                : model.CalendarId.Trim();

            settings.RequireApprovalAfterOfficeHours = model.RequireApprovalAfterOfficeHours;
            settings.AllowAutoReplyAfterOfficeHours = model.AllowAutoReplyAfterOfficeHours;

            await _dbContext.SaveChangesAsync(cancellationToken);

            return MapToViewModel(settings);
        }

        private async Task<ReminderAutomationSettings> GetOrCreateSettingsAsync(CancellationToken cancellationToken)
        {
            var settings = await _dbContext.Set<ReminderAutomationSettings>()
                .FirstOrDefaultAsync(settingEntity => settingEntity.Id == 1, cancellationToken);

            if (settings != null)
            {
                return settings;
            }

            settings = new ReminderAutomationSettings
            {
                Id = 1
            };

            _dbContext.Set<ReminderAutomationSettings>().Add(settings);
            await _dbContext.SaveChangesAsync(cancellationToken);

            return settings;
        }

        private static ReminderSettingsViewModel MapToViewModel(ReminderAutomationSettings settings)
        {
            return new ReminderSettingsViewModel
            {
                Enabled = settings.Enabled,
                ScanIntervalMinutes = settings.ScanIntervalMinutes,
                DefaultReminderAfterMinutes = settings.DefaultReminderAfterMinutes,
                KeywordsCsv = settings.KeywordsCsv,
                GmailQuery = settings.GmailQuery,
                AutoReplyEnabled = settings.AutoReplyEnabled,
                AutoReplyKeywordsCsv = settings.AutoReplyKeywordsCsv,
                AiDailyLimit = settings.AiDailyLimit,
                OfficeStartHour = settings.OfficeStartHour,
                OfficeEndHour = settings.OfficeEndHour,
                SlotMinutes = settings.SlotMinutes,
                TimezoneId = settings.TimezoneId,
                CalendarEnabled = settings.CalendarEnabled,
                CalendarId = settings.CalendarId,
                RequireApprovalAfterOfficeHours = settings.RequireApprovalAfterOfficeHours,
                AllowAutoReplyAfterOfficeHours = settings.AllowAutoReplyAfterOfficeHours,

                // Read-only status values
                LastRunOn = settings.LastRunOn,
                LastRunCreatedCount = settings.LastRunCreatedCount,
                LastRunStatus = settings.LastRunStatus,
                LastRunError = settings.LastRunError,
                AiCallsToday = settings.AiCallsToday,
                AiUsageDayUtc = settings.AiUsageDayUtc,
                AiPausedUntilUtc = settings.AiPausedUntilUtc,
                AiLastError = settings.AiLastError
            };
        }

        private static void Validate(ReminderSettingsViewModel       model)
        {
            if (model.ScanIntervalMinutes <= 0)
            {
                throw new InvalidOperationException("ScanIntervalMinutes must be greater than 0.");
            }

            if (model.DefaultReminderAfterMinutes <= 0)
            {
                throw new InvalidOperationException("DefaultReminderAfterMinutes must be greater than 0.");
            }

            if (model.AiDailyLimit < 0)
            {
                throw new InvalidOperationException("AiDailyLimit cannot be negative.");
            }

            if (model.OfficeStartHour < 0 || model.OfficeStartHour > 23)
            {
                throw new InvalidOperationException("OfficeStartHour must be between 0 and 23.");
            }

            if (model.OfficeEndHour < 1 || model.OfficeEndHour > 24)
            {
                throw new InvalidOperationException("OfficeEndHour must be between 1 and 24.");
            }

            if (model.OfficeStartHour >= model.OfficeEndHour)
            {
                throw new InvalidOperationException("OfficeStartHour must be less than OfficeEndHour.");
            }

            if (model.SlotMinutes <= 0)
            {
                throw new InvalidOperationException("SlotMinutes must be greater than 0.");
            }
        }
    }

}