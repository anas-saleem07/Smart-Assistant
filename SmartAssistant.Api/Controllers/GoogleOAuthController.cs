using Microsoft.AspNetCore.Mvc;
using SmartAssistant.Api.Models;
using SmartAssistant.Api.Services.Email;
using SmartAssistant.Api.Services.Google;
using System.Net;

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
            Console.WriteLine("CALLBACK ENDPOINT HIT");
            var normalizedPlatform = NormalizePlatform(platform);
            var state = normalizedPlatform + ":" + Guid.NewGuid().ToString("N");

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

                Console.WriteLine("STATUS ENDPOINT HIT");
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
                {
                    return Content(
                        BuildDeepLinkHtml(
                            AppMissingCodeDeepLink,
                            "Google sign-in failed",
                            "Missing authorization code. Tap the button below to return to the app."),
                        "text/html");
                }

                var platform = ExtractPlatformFromState(state);

                await _emailOAuthService.HandleGoogleCallbackAsync(code, platform, ct);

                return Content(
                    BuildDeepLinkHtml(
                        AppSuccessDeepLink,
                        "Gmail connected",
                        "Your Gmail account is connected. Tap the button below if the app does not open automatically."),
                    "text/html");
            }
            catch (Exception ex)
            {
                var safeMessage = Uri.EscapeDataString(ex.Message);
                var errorDeepLink = "smartassistant://google-auth-complete?status=error&message=" + safeMessage;

                return Content(
                    BuildDeepLinkHtml(
                        errorDeepLink,
                        "Google sign-in failed",
                        "Something went wrong. Tap the button below to return to the app."),
                    "text/html");
            }
        }

        [HttpGet("~/api/email-oauth/gmail/callback")]
        public async Task<IActionResult> LegacyCallback([FromQuery] string code, [FromQuery] string? state, CancellationToken ct)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(code))
                {
                    return Content(
                        BuildDeepLinkHtml(
                            AppMissingCodeDeepLink,
                            "Google sign-in failed",
                            "Missing authorization code. Tap the button below to return to the app."),
                        "text/html");
                }

                var platform = ExtractPlatformFromState(state);

                await _emailOAuthService.HandleGoogleCallbackAsync(code, platform, ct);

                return Content(
                    BuildDeepLinkHtml(
                        AppSuccessDeepLink,
                        "Gmail connected",
                        "Your Gmail account is connected. Tap the button below if the app does not open automatically."),
                    "text/html");
            }
            catch (Exception ex)
            {
                var safeMessage = Uri.EscapeDataString(ex.Message);
                var errorDeepLink = "smartassistant://google-auth-complete?status=error&message=" + safeMessage;

                return Content(
                    BuildDeepLinkHtml(
                        errorDeepLink,
                        "Google sign-in failed",
                        "Something went wrong. Tap the button below to return to the app."),
                    "text/html");
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

        private static string BuildDeepLinkHtml(string appDeepLink, string title, string message)
        {
            var safeTitle = WebUtility.HtmlEncode(title);
            var safeMessage = WebUtility.HtmlEncode(message);
            var safeDeepLink = WebUtility.HtmlEncode(appDeepLink);

            return "<!DOCTYPE html>" +
                   "<html>" +
                   "<head>" +
                   "<meta charset=\"utf-8\" />" +
                   "<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\" />" +
                   "<title>" + safeTitle + "</title>" +
                   "</head>" +
                   "<body style=\"font-family: Arial, sans-serif; padding: 32px; background: #0b0f1a; color: #e8ecff;\">" +
                   "<div style=\"max-width: 520px; margin: 0 auto;\">" +
                   "<h2>" + safeTitle + "</h2>" +
                   "<p>" + safeMessage + "</p>" +
                   "<p><a href=\"" + safeDeepLink + "\" " +
                   "style=\"display:inline-block; padding:12px 16px; border-radius:12px; text-decoration:none; font-weight:700; background:#7c5cff; color:#ffffff;\">" +
                   "Return to SmartAssistant</a></p>" +
                   "</div>" +
                   "<script>" +
                     "setTimeout(function(){ window.location.href = '" + JavaScriptStringEncode(appDeepLink) + "'; },     250);"  +
                     "</script>" +
                   "</body>" +
                   "</html>";
        }

        private static string JavaScriptStringEncode(string value)
        {
            return value
                .Replace("\\", "\\\\")
                .Replace("'", "\\'");
        }
    }
}