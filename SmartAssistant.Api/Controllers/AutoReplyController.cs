using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using SmartAssistant.Api.Services.AutoReply;

namespace SmartAssistant.Api.Controllers
{
    [ApiController]
    [Route("api/auto-reply")]
    public class AutoReplyController : ControllerBase
    {
        private readonly IAutoReplyService _autoReply;

        public AutoReplyController(IAutoReplyService autoReply)
        {
            _autoReply = autoReply;
        }

        // Call this when you click YES in UI (or from Swagger)
        [HttpPost("approve/{emailProcessedId:long}")]
        public async Task<IActionResult> Approve(long emailProcessedId, CancellationToken ct)
        {
            var ok = await _autoReply.SendApprovedReplyAsync(emailProcessedId, ct);
            return Ok(new { success = ok });
        }
    }
}