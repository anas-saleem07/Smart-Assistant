using Hangfire;
using Microsoft.Extensions.Logging;
using SmartAssistant.Api.Services.Automation;

namespace SmartAssistant.Api.Jobs
{
    [DisableConcurrentExecution(600)]
    public sealed class ReminderAutomationJob
    {
        private readonly IReminderAutomationService _automation;
        private readonly ILogger<ReminderAutomationJob> _logger;

        public ReminderAutomationJob(
            IReminderAutomationService automation,
            ILogger<ReminderAutomationJob> logger)
        {
            _automation = automation;
            _logger = logger;
        }

        public async Task Run(CancellationToken ct)
        {
            _logger.LogInformation("Hangfire ReminderAutomationJob started.");

            var created = await _automation.ScanAndCreateRemindersAsync(ct);

            _logger.LogInformation("Hangfire ReminderAutomationJob finished. Reminders created: {Created}", created);
        }
    }
}