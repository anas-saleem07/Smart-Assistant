using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using SmartAssistant.Api.Services.AutoReply;
using SmartAssistant.Api.Services.Email;

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

        [HttpGet("pending")]
        public async Task<ActionResult<List<PendingAutoReplyDto>>> GetPending(CancellationToken ct)
        {
            var items = await _autoReply.GetPendingApprovalsAsync(ct);
            return Ok(items);
        }

        [HttpPost("approve-same-slot/{emailProcessedId:long}")]
        public async Task<IActionResult> ApproveSameSlot(long emailProcessedId, CancellationToken ct, EmailMessage email)
        {
            var ok = await _autoReply.ApprovePendingReplyAsync(emailProcessedId, false, ct, email);
            return Ok(new { success = ok });
        }

        [HttpPost("approve-suggested-slot/{emailProcessedId:long}")]
        public async Task<IActionResult> ApproveSuggestedSlot(long emailProcessedId, CancellationToken ct, EmailMessage email)
        {
            var ok = await _autoReply.ApprovePendingReplyAsync(emailProcessedId, true, ct,email);
            return Ok(new { success = ok });
        }

        [HttpPost("reject/{emailProcessedId:long}")]
        public async Task<IActionResult> Reject(long emailProcessedId, CancellationToken ct, EmailMessage email)
        {
            var ok = await _autoReply.RejectPendingReplyAsync(emailProcessedId, ct, email);
            return Ok(new { success = ok });
        }
        
        [HttpPost("open-suggested-calendar/{emailProcessedId:long}")]
        public async Task<ActionResult<ApprovalCalendarOpenDto>> OpenSuggestedCalendar(long emailProcessedId, CancellationToken ct)
        {
            var result = await _autoReply.CreateSuggestedCalendarEventAsync(emailProcessedId, ct);
            return Ok(result);
        }
    }
}