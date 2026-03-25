using Microsoft.AspNetCore.Mvc;
using SmartAssistant.Api.Models;
using SmartAssistant.Api.Services.Email;
using SmartAssistant.Api.Services.Google;

namespace SmartAssistant.Api.Controllers
{
    [ApiController]
    [Route("api/google-oauth")]
    public class GoogleOAuthController : ControllerBase
    {
        private const string AppSuccessDeepLink = "smartassistant://google-auth-complete?status=success";
        private const string AppMissingCodeDeepLink = "smartassistant://google-auth-complete?status=error&message=missing_code";

        private readonly IEmailOAuthService _emailOAuthService;
        private readonly IGoogleConnectionService _googleConnectionService;

        public GoogleOAuthController(
            IEmailOAuthService emailOAuthService,
            IGoogleConnectionService googleConnectionService)
        {
            _emailOAuthService = emailOAuthService;
            _googleConnectionService = googleConnectionService;
        }

        [HttpGet("connect")]
        public IActionResult Connect([FromQuery] string? platform = "windows")
        {
            var normalizedPlatform = NormalizePlatform(platform);

            // Keep platform inside state so callback knows which redirect URI was used.
            var state = $"{normalizedPlatform}:{Guid.NewGuid():N}";

            var url = _emailOAuthService.GenerateGoogleAuthUrl(state, normalizedPlatform);

            return Ok(new
            {
                url
            });
        }

        [HttpGet("status")]
        public async Task<ActionResult<GoogleConnectionStatusModel>> GetStatus(CancellationToken ct)
        {
            try
            {
                var status = await _googleConnectionService.GetStatusAsync(ct);
                return Ok(status);
            }
            catch (Exception ex)
            {
                return BadRequest(new ApiErrorResponse
                {
                    Code = "gmail_status_failed",
                    Message = "GET /api/google-oauth/status failed: " + ex.Message
                });
            }
        }

        [HttpPost("disconnect")]
        public async Task<IActionResult> Disconnect(CancellationToken ct)
        {
            try
            {
                var success = await _googleConnectionService.DisconnectAsync(ct);

                return Ok(new
                {
                    success,
                    message = "Gmail disconnected successfully."
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new ApiErrorResponse
                {
                    Code = "gmail_disconnect_failed",
                    Message = "POST /api/google-oauth/disconnect failed: " + ex.Message
                });
            }
        }

        [HttpGet("gmail/callback")]
        public async Task<IActionResult> Callback([FromQuery] string code, [FromQuery] string? state, CancellationToken ct)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(code))
                    return Redirect(AppMissingCodeDeepLink);

                var platform = ExtractPlatformFromState(state);

                await _emailOAuthService.HandleGoogleCallbackAsync(code, platform, ct);

                return Redirect(AppSuccessDeepLink);
            }
            catch (Exception ex)
            {
                var safeMessage = Uri.EscapeDataString(ex.Message);
                return Redirect($"smartassistant://google-auth-complete?status=error&message={safeMessage}");
            }
        }

        [HttpGet("~/api/email-oauth/gmail/callback")]
        public async Task<IActionResult> LegacyCallback([FromQuery] string code, [FromQuery] string? state, CancellationToken ct)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(code))
                    return Redirect(AppMissingCodeDeepLink);

                var platform = ExtractPlatformFromState(state);

                await _emailOAuthService.HandleGoogleCallbackAsync(code, platform, ct);

                return Redirect(AppSuccessDeepLink);
            }
            catch (Exception ex)
            {
                var safeMessage = Uri.EscapeDataString(ex.Message);
                return Redirect($"smartassistant://google-auth-complete?status=error&message={safeMessage}");
            }
        }

        private static string NormalizePlatform(string? platform)
        {
            return string.Equals(platform, "android", StringComparison.OrdinalIgnoreCase)
                ? "android"
                : "windows";
        }

        private static string ExtractPlatformFromState(string? state)
        {
            if (string.IsNullOrWhiteSpace(state))
                return "windows";

            var parts = state.Split(':', 2, StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length == 0)
                return "windows";

            return NormalizePlatform(parts[0]);
        }
    }
}