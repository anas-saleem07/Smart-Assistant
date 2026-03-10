using System;

namespace SmartAssistant.Api.Contracts.Email
{
    public sealed class InboundEmailMessage
    {
        /// <summary>
        /// Email provider name, for example Gmail or Outlook.
        /// </summary>
        public string Provider { get; set; } = "Gmail";

        /// <summary>
        /// Provider-specific message id.
        /// </summary>
        public string Id { get; set; } = default!;

        /// <summary>
        /// Email subject line.
        /// </summary>
        public string? Subject { get; set; }

        /// <summary>
        /// Short extracted preview/snippet of the email body.
        /// </summary>
        public string? Snippet { get; set; }

        /// <summary>
        /// Sender display/email string.
        /// </summary>
        public string? From { get; set; }

        /// <summary>
        /// Optional structured invite details.
        /// Null means either there is no invite or it was not extracted.
        /// </summary>
        public EmailCalendarInvite? CalendarInvite { get; set; }
    }
}