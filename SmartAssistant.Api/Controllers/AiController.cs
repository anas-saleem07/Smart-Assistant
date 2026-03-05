using SmartAssistant.Api.Services.Ai;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using SmartAssistant.Api.Options;
using Microsoft.Extensions.Options;
namespace SmartAssistant.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AiController : ControllerBase
    {
        private readonly IAiClient _ai;
        private readonly IHttpClientFactory _httpFactory;
        private readonly GeminiOptions _opt;

        public AiController(IAiClient ai, IHttpClientFactory httpFactory, IOptions<GeminiOptions> opt)
        {
            _ai = ai;
            _httpFactory = httpFactory;
            _opt = opt.Value;
        }

        [HttpGet("ping")]
        public async Task<IActionResult> Ping(CancellationToken ct)
        {
            var text = await _ai.GenerateAsync("Say pong in one word.", ct);
            return Ok(new { result = text });
        }

        [HttpGet("models")]
        public async Task<IActionResult> Models(CancellationToken ct)
        {
            // What: Lists models available for your API key
            // Why: Prevents guessing model names (Google changes them over time)

            var http = _httpFactory.CreateClient();

            var url = "https://generativelanguage.googleapis.com/v1beta/models?key=" + _opt.ApiKey;
            var resp = await http.GetAsync(url, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);

            if (!resp.IsSuccessStatusCode)
                return BadRequest(new { status = (int)resp.StatusCode, body });

            using var doc = JsonDocument.Parse(body);

            var models = doc.RootElement.GetProperty("models")
                .EnumerateArray()
                .Select(m => new
                {
                    name = m.GetProperty("name").GetString(), // e.g. "models/gemini-2.5-flash"
                    displayName = m.TryGetProperty("displayName", out var dn) ? dn.GetString() : null,
                    supported = m.TryGetProperty("supportedGenerationMethods", out var sgm)
                        ? sgm.EnumerateArray().Select(x => x.GetString()).ToList()
                        : new List<string>()
                })
                .ToList();

            return Ok(models);
        }
    }
}