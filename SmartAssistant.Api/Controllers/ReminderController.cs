using AutoMapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using SmartAssistant.Api.Data;
using SmartAssistant.Api.Models;
using SmartAssistant.Api.Services;
using SmartAssistant.Api.Services.Automation;
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

        public ReminderController(IReminderService service, IMapper mapper, IReminderAutomationService automation, ApplicationDbContext db)
        {
            _service = service;
            _mapper = mapper;
            _automation = automation;
            _db = db;
        }

        [HttpPost("create")]
        public async Task<IActionResult> Create([FromBody] ReminderViewModel vm)
        {
            if(!ModelState.IsValid)
                return BadRequest(ModelState);
            var reminder = _mapper.Map<Core.Entities.Reminder>(vm);
            await _service.AddReminderAsync(reminder);

            return Ok(reminder); // or map to a DTO
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