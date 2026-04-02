using AutoMapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartAssistant.Api.Data;
using SmartAssistant.Api.Helpers;
using SmartAssistant.Api.Models;
using SmartAssistant.Api.Services;
using SmartAssistant.Api.Services.Automation;
using SmartAssistant.Api.Services.Calendar;
using SmartAssistant.Core.Entities;

namespace SmartAssistant.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ReminderController : ControllerBase
    {
        private readonly IReminderService _service;
        private readonly IMapper _mapper;
        private readonly IReminderAutomationService _automation;
        private readonly ApplicationDbContext _db;
        private readonly ICalendarService _calendar;

        public ReminderController(
            IReminderService service,
            IMapper mapper,
            IReminderAutomationService automation,
            ApplicationDbContext db,
            ICalendarService calendar)
        {
            _service = service;
            _mapper = mapper;
            _automation = automation;
            _db = db;
            _calendar = calendar;
        }

        [HttpPost("create")]
        public async Task<IActionResult> Create([FromBody] ReminderViewModel vm, CancellationToken ct)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var reminder = _mapper.Map<Reminder>(vm);

            // Load settings first so local reminder time can be converted to UTC correctly
            var settings = await _db.ReminderAutomationSettings.FirstAsync(x => x.Id == 1, ct);

            // Convert local UI time (e.g. Karachi time) to UTC before saving
            reminder.ReminderTime = AppTimeHelper.ConvertLocalDateTimeToUtc(
                DateTime.SpecifyKind(vm.ReminderTime.DateTime, DateTimeKind.Unspecified),
                settings.TimezoneId);

            // Save reminder (manual reminder)
            await _service.AddManualReminderAsync(reminder);

            // Calendar is mandatory for manual reminders too
            if (string.IsNullOrWhiteSpace(reminder.CalendarEventId))
            {
                try
                {
                    var eventId = await _calendar.CreateEventAsync(reminder, settings, ct);

                    reminder.CalendarEventId = eventId;
                    reminder.CalendarSyncedOn = DateTimeOffset.UtcNow;
                    reminder.CalendarSyncError = null;

                    await _db.SaveChangesAsync(ct);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    reminder.CalendarSyncError = ex.Message;
                    await _db.SaveChangesAsync(ct);
                }
            }

            return Ok(reminder);
        }

        [HttpGet("list")]
        public async Task<ActionResult<List<ReminderViewModel>>> GetReminders()
        {
            var reminders = await _service.GetAllAsync();

            var settings = await _db.ReminderAutomationSettings.FirstAsync(x => x.Id == 1);

            var items = reminders.Select(reminder => new ReminderViewModel
            {
                Id = reminder.Id,
                Title = reminder.Title,
                Description = reminder.Description,
                ReminderTime = reminder.ReminderTime,
                ReminderTimeLocalText = AppTimeHelper.FormatUtcAsLocal(
                    reminder.ReminderTime,
                    settings.TimezoneId,
                    "ddd, dd MMM yyyy hh:mm tt"),
                Completed = reminder.Completed,
                CreatedOn = reminder.CreatedOn
            }).ToList();

            return items;
        }

        [HttpPut("status/{id:guid}")]
        public async Task<IActionResult> UpdateStatus(Guid id, [FromBody] UpdateReminderStatusRequest request)
        {
            var result = await _service.SetCompletedAsync(id, request.Completed);

            if (!result.Success)
                return BadRequest(new { message = result.ErrorMessage });

            return Ok(new { message = "Reminder status updated successfully." });
        }

        [HttpDelete("delete/{id:guid}")]
        public async Task<IActionResult> Delete(Guid id)
        {
            var result = await _service.DeleteAsync(id);
            if (!result)
                return NotFound();

            return NoContent();
        }

        [HttpPost("automation/scan")]
        public async Task<IActionResult> ScanNow(CancellationToken ct)
        {
            var created = await _automation.ScanAndCreateRemindersAsync(ct);
            return Ok(new { created });
        }

        [HttpGet("automation/settings")]
        public async Task<IActionResult> GetAutomationSettings(CancellationToken ct)
        {
            var settings = await _db.ReminderAutomationSettings.FirstAsync(x => x.Id == 1, ct);
            return Ok(settings);
        }

        [HttpPut("automation/settings")]
        public async Task<IActionResult> UpdateAutomationSettings([FromBody] ReminderAutomationSettings payload, CancellationToken ct)
        {
            var settings = await _db.ReminderAutomationSettings.FirstAsync(x => x.Id == 1, ct);

            settings.Enabled = payload.Enabled;
            settings.ScanIntervalMinutes = Math.Max(1, payload.ScanIntervalMinutes);
            settings.DefaultReminderAfterMinutes = Math.Max(1, payload.DefaultReminderAfterMinutes);
            settings.KeywordsCsv = payload.KeywordsCsv ?? settings.KeywordsCsv;

            await _db.SaveChangesAsync(ct);
            return Ok(settings);
        }

        public sealed class UpdateReminderStatusRequest
        {
            public bool Completed { get; set; }
        }
    }
}