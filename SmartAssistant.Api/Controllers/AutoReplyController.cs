using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using SmartAssistant.Api.Models;
using SmartAssistant.Api.Services.AutoReply;
using SmartAssistant.Api.Services.Google;

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
            try
            {
                var items = await _autoReply.GetPendingApprovalsAsync(ct);
                return Ok(items);
            }
            catch (GoogleOAuthReconnectRequiredException)
            {
                return Unauthorized(new
                {
                    code = "gmail_reconnect_required",
                    message = "Gmail account needs reconnect."
                });
            }
            catch (Exception ex)
            {
                return BadRequest("GET /api/auto-reply/pending failed: " + ex.Message);
            }
        }

        [HttpPost("approve-same-slot/{id:long}")]
        public async Task<IActionResult> ApproveSameSlot(long id, CancellationToken ct)
        {
            var success = await _autoReply.ApprovePendingReplyAsync(id, false, ct);
            if (!success)
                return BadRequest("Unable to approve original slot.");

            return Ok(new { success = true, message = "Original slot approved." });
        }

        [HttpPost("approve-suggested-slot/{id:long}")]
        public async Task<IActionResult> ApproveSuggestedSlot(long id, CancellationToken ct)
        {
            var success = await _autoReply.ApprovePendingReplyAsync(id, true, ct);
            if (!success)
                return BadRequest("Unable to approve suggested slot.");

            return Ok(new { success = true, message = "Suggested slot sent." });
        }

        [HttpPost("reject/{emailProcessedId:long}")]
        public async Task<IActionResult> Reject(long emailProcessedId, CancellationToken ct)
        {
            try
            {
                var ok = await _autoReply.RejectPendingReplyAsync(emailProcessedId, ct);
                return Ok(new { success = ok });
            }
            catch (GoogleOAuthReconnectRequiredException ex)
            {
                return Unauthorized(new ApiErrorResponse
                {
                    Code = "gmail_reconnect_required",
                    Message = ex.Message
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new ApiErrorResponse
                {
                    Code = "reject_failed",
                    Message = "POST /api/auto-reply/reject failed: " + ex.Message
                });
            }
        }

        [HttpPost("open-suggested-calendar/{emailProcessedId:long}")]
        public async Task<ActionResult<ApprovalCalendarOpenDto>> OpenSuggestedCalendar(long emailProcessedId, CancellationToken ct)
        {
            try
            {
                var result = await _autoReply.CreateSuggestedCalendarEventAsync(emailProcessedId, ct);
                return Ok(result);
            }
            catch (GoogleOAuthReconnectRequiredException ex)
            {
                return Unauthorized(new ApiErrorResponse
                {
                    Code = "gmail_reconnect_required",
                    Message = ex.Message
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new ApiErrorResponse
                {
                    Code = "open_suggested_calendar_failed",
                    Message = "POST /api/auto-reply/open-suggested-calendar failed: " + ex.Message
                });
            }
        }

        [HttpGet("history")]
        public async Task<ActionResult<List<ProcessedEmailHistoryDto>>> GetHistory(CancellationToken ct)
        {
            try
            {
                var items = await _autoReply.GetProcessedEmailHistoryAsync(ct);
                return Ok(items);
            }
            catch (GoogleOAuthReconnectRequiredException)
            {
                return Unauthorized(new
                {
                    code = "gmail_reconnect_required",
                    message = "Gmail account needs reconnect."
                });
            }
            catch (Exception ex)
            {
                return BadRequest("GET /api/auto-reply/history failed: " + ex.Message);
            }
        }
    }
}