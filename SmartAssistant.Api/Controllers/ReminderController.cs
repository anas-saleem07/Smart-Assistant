using AutoMapper;
using Google.Apis.Calendar.v3;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using SmartAssistant.Api.Data;
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

        public ReminderController(IReminderService service, IMapper mapper, IReminderAutomationService automation, ApplicationDbContext db, ICalendarService calendar)
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

            // Save reminder (manual reminder)
            await _service.AddManualReminderAsync(reminder);

            // Calendar is mandatory for manual reminders too
            var settings = await _db.ReminderAutomationSettings.FirstAsync(x => x.Id == 1, ct);

            // Prevent duplicate calendar sync
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
        public async Task<IActionResult> GetAll() =>
            Ok(await _service.GetAllAsync());

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
    }
}