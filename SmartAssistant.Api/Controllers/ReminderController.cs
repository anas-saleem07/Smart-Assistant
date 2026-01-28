using AutoMapper;
using Microsoft.AspNetCore.Mvc;
using SmartAssistant.Api.Models;
using SmartAssistant.Api.Services;


namespace SmartAssistant.Api.Controllers
{
    public class ReminderController : Controller
    {
        private readonly IReminderService _Service;
        private readonly IMapper _mapper;

        public ReminderController(IReminderService service, IMapper mapper)
        {
            _Service = service;
            _mapper = mapper;
        }
        public IActionResult Index()
        {
            return View();
        }
        [HttpPost]
        public async Task<IActionResult> Create(ReminderViewModel reminderViewModel)
        {
            if (ModelState.IsValid)
            {
                var reminder = _mapper.Map<ReminderViewModel, Core.Entities.Reminder>(reminderViewModel);
                await _Service.AddReminderAsync(reminder);
                return RedirectToAction("Index");
            }
            return View(reminderViewModel);
        }

        [HttpGet]
        public async Task<IActionResult> GetAll() =>
            Ok(await _Service.GetAllAsync());

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(int id)
        {
            var result = await _Service.DeleteAsync(id);
            if (!result)
                return NotFound();
            return NoContent();
        }
    }   
}