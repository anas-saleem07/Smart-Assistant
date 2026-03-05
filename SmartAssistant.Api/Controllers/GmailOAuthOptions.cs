using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/email-oauth")]
public class EmailOAuthController : ControllerBase
{
    private readonly IEmailOAuthService _oauthService;

    public EmailOAuthController(IEmailOAuthService oauthService)
    {
        _oauthService = oauthService;
    }

    [HttpGet("connect-google")]
    public IActionResult ConnectGoogle()
    {
        var state = Guid.NewGuid().ToString(); // track request
        var url = _oauthService.GenerateGoogleAuthUrl(state);

        return Ok(new { url });
    }

    [HttpGet("gmail/callback")]
    public async Task<IActionResult> GmailCallback([FromQuery] string code, CancellationToken ct)
    {
        await _oauthService.HandleGoogleCallbackAsync(code, ct);
        return Ok("Gmail connected and refresh token saved.");
    }
}