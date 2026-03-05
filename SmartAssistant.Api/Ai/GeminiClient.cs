using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using SmartAssistant.Api.Options;

namespace SmartAssistant.Api.Services.Ai
{
    public interface IAiClient
    {
        Task<string> GenerateAsync(string prompt, CancellationToken ct);
    }

    public sealed class GeminiClient : IAiClient
    {
        private readonly HttpClient _http;
        private readonly GeminiOptions _opt;

        public GeminiClient(HttpClient http, IOptions<GeminiOptions> opt)
        {
            _http = http;
            _opt = opt.Value;
        }

        public async Task<string> GenerateAsync(string prompt, CancellationToken ct)
        {
            // Use v1. v1beta often causes model/endpoint mismatch for some keys.
            var cleanModel = _opt.Model?.Trim() ?? "";
            if (cleanModel.StartsWith("models/", StringComparison.OrdinalIgnoreCase))
            {
                cleanModel = cleanModel.Substring("models/".Length);
            }

            var url =
                "https://generativelanguage.googleapis.com/" +
                _opt.ApiVersion +
                "/models/" +
                cleanModel +
                ":generateContent?key=" + _opt.ApiKey;

            var payload = new
            {
                contents = new[]
                {
                    new
                    {
                        role = "user",
                        parts = new[]
                        {
                            new { text = prompt }
                        }
                    }
                }
            };

            var json = JsonSerializer.Serialize(payload);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");

            using var resp = await _http.PostAsync(url, content, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);

            if (!resp.IsSuccessStatusCode)
            {
                // Show the actual Gemini error so we can fix it quickly
                throw new InvalidOperationException(
                    "Gemini call failed. Status=" + (int)resp.StatusCode + ". Body=" + body);
            }

            using var doc = JsonDocument.Parse(body);

            // Standard response parsing
            var text = doc.RootElement
                .GetProperty("candidates")[0]
                .GetProperty("content")
                .GetProperty("parts")[0]
                .GetProperty("text")
                .GetString();

            return text ?? "";
        }
    }
}