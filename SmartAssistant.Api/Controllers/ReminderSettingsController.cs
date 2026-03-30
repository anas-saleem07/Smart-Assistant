using Microsoft.AspNetCore.Mvc;
using SmartAssistant.Api.Models;
using SmartAssistant.Api.Services.Settings;

namespace SmartAssistant.Api.Controllers
{
    [ApiController]
    [Route("api/reminder-settings")]
    public sealed class ReminderSettingsController : ControllerBase
    {
        private readonly IReminderSettingService _reminderAutomationSettingsService;

        public ReminderSettingsController(IReminderSettingService reminderAutomationSettingsService)
        {
            _reminderAutomationSettingsService = reminderAutomationSettingsService;
        }

        [HttpGet]
        public async Task<ActionResult<ReminderSettingsViewModel>> Get(CancellationToken cancellationToken)
        {
            var model = await _reminderAutomationSettingsService.GetAsync(cancellationToken);
            return Ok(model);
        }

        [HttpPut]
        public async Task<ActionResult<ReminderSettingsViewModel>> Update(
            [FromBody]  ReminderSettingsViewModel model,
            CancellationToken cancellationToken)
        {
            var updatedModel = await _reminderAutomationSettingsService.UpdateAsync(model, cancellationToken);
            return Ok(updatedModel);
        }
    }
}