using System;

namespace SmartAssistant.Api.Contracts.Email
{
    public sealed class EmailCalendarInvite
    {
        /// <summary>
        /// Google Calendar event id, if available from provider parsing.
        /// </summary>
        public string? CalendarEventId { get; set; }

        /// <summary>
        /// Invite start time in UTC.
        /// </summary>
        public DateTimeOffset? StartUtc { get; set; }

        /// <summary>
        /// Invite end time in UTC.
        /// </summary>
        public DateTimeOffset? EndUtc { get; set; }

        /// <summary>
        /// What:
        /// Indicates whether the invite contains a valid time range.
        ///
        /// Why:
        /// Helps business logic safely decide whether to trust this structured invite info.
        /// </summary>
        public bool HasValue =>
            StartUtc.HasValue &&
            EndUtc.HasValue &&
            EndUtc.Value > StartUtc.Value;
    }
}