using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SmartAssistant.Api.Services.Automation;

namespace SmartAssistant.Api.Workers
{
    public class ReminderAutomationWorker : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;

        public ReminderAutomationWorker(IServiceScopeFactory scopeFactory)
        {
            _scopeFactory = scopeFactory;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                using var scope = _scopeFactory.CreateScope();
                var automation = scope.ServiceProvider.GetRequiredService<IReminderAutomationService>();
                var db = scope.ServiceProvider.GetRequiredService<SmartAssistant.Api.Data.ApplicationDbContext>();

                // read interval from DB
                var settings = await db.ReminderAutomationSettings.FirstAsync(x => x.Id == 1, stoppingToken);
                var delayMinutes = Math.Max(1, settings.ScanIntervalMinutes);

                if (settings.Enabled)
                {
                    try
                    {
                        await automation.ScanAndCreateRemindersAsync(stoppingToken);
                    }
                    catch
                    {
                        // log here (ILogger) - do not crash worker
                    }
                }

                await Task.Delay(TimeSpan.FromMinutes(delayMinutes), stoppingToken);
            }
        }
    }
}