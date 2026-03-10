using Google.Apis.Calendar.v3;
using Google.Apis.Services;
using Microsoft.EntityFrameworkCore;
using SmartAssistant.Api.Data;
using SmartAssistant.Api.Helpers;
using SmartAssistant.Api.Services.Google;
using SmartAssistant.Core.Entities;
using System;
using System.Linq;
using GoogleCalendarEvent = global::Google.Apis.Calendar.v3.Data.Event;

namespace SmartAssistant.Api.Services.Calendar
{
    public sealed class GoogleCalendarService : ICalendarService
    {

        private readonly ApplicationDbContext _db;
        private readonly IOAuthTokenHelper _tokenHelper;

        public GoogleCalendarService(ApplicationDbContext db, IOAuthTokenHelper tokenHelper)
        {
            _db = db;
            _tokenHelper = tokenHelper;
        }

        // Keep your existing CreateEventAsync implementation here.
        public async Task<string> CreateEventAsync(Reminder reminder, ReminderAutomationSettings settings, CancellationToken ct)
        {
            throw new NotImplementedException("CreateEventAsync is already implemented in your project. Keep your existing code.");
        }

        public async Task<bool> IsFreeAsync(DateTimeOffset startUtc, DateTimeOffset endUtc, ReminderAutomationSettings settings, CancellationToken ct)
        {
            if (endUtc <= startUtc)
                return true;

            var calendarId = string.IsNullOrWhiteSpace(settings.CalendarId) ? "primary" : settings.CalendarId;

            var service = await CreateCalendarClientAsync(ct);

            var request = service.Events.List(calendarId);
            request.TimeMin = startUtc.UtcDateTime;
            request.TimeMax = endUtc.UtcDateTime;
            request.SingleEvents = true;
            request.ShowDeleted = false;
            request.MaxResults = 5;

            var events = await request.ExecuteAsync(ct);

            // If any event overlaps, not free
            return events.Items == null || events.Items.Count == 0;
        }

        public async Task<DateTimeOffset?> FindNextFreeSlotAsync(DateTimeOffset fromUtc, ReminderAutomationSettings settings, CancellationToken ct)
        {
            var targetTimeZone = AppTimeHelper.ResolveTimeZone(settings.TimezoneId);

            // Convert to local time in selected timezone
            var fromLocal = TimeZoneInfo.ConvertTime(fromUtc, targetTimeZone);

            // Search up to next 7 days
            for (int dayOffset = 0; dayOffset < 7; dayOffset++)
            {
                // fromLocal.Date is DateTime (local date)
                var dayLocalDate = fromLocal.Date.AddDays(dayOffset);

                // Important: TimeZoneInfo offsets should be calculated using a DateTime with Kind=Unspecified
                // because "dayLocalDate" represents local wall-clock time in that timezone.
                var dayLocalUnspecified = DateTime.SpecifyKind(dayLocalDate, DateTimeKind.Unspecified);

                var officeStartLocalDateTime = dayLocalUnspecified.AddHours(settings.OfficeStartHour);
                var officeEndLocalDateTime = dayLocalUnspecified.AddHours(settings.OfficeEndHour);

                var officeStartOffset = targetTimeZone.GetUtcOffset(officeStartLocalDateTime);
                var officeEndOffset = targetTimeZone.GetUtcOffset(officeEndLocalDateTime);

                var officeStartLocal = new DateTimeOffset(officeStartLocalDateTime, officeStartOffset);
                var officeEndLocal = new DateTimeOffset(officeEndLocalDateTime, officeEndOffset);

                // If dayOffset == 0, start at max(current time, office start)
                var cursorLocal = officeStartLocal;
                if (dayOffset == 0 && fromLocal > officeStartLocal)
                    cursorLocal = fromLocal;

                // Round cursor to next slot boundary
                cursorLocal = RoundUpToSlot(cursorLocal, settings.SlotMinutes);

                while (cursorLocal.AddMinutes(settings.SlotMinutes) <= officeEndLocal)
                {
                    var startUtc = cursorLocal.ToUniversalTime();
                    var endUtc = cursorLocal.AddMinutes(settings.SlotMinutes).ToUniversalTime();

                    var free = await IsFreeAsync(startUtc, endUtc, settings, ct);
                    if (free)
                        return startUtc;

                    cursorLocal = cursorLocal.AddMinutes(settings.SlotMinutes);
                }
            }

            return null;
        }

        public async Task<DateTimeOffset?> FindNextFreeSlotOnSameDayAsync(DateTimeOffset preferredStartUtc, ReminderAutomationSettings settings, CancellationToken ct)
        {
            var targetTimeZone = AppTimeHelper.ResolveTimeZone(settings.TimezoneId);
            var preferredLocal = TimeZoneInfo.ConvertTime(preferredStartUtc, targetTimeZone);

            var dayLocalDate = preferredLocal.Date;

            var dayLocalUnspecified = DateTime.SpecifyKind(dayLocalDate, DateTimeKind.Unspecified);

            var officeStartLocalDateTime = dayLocalUnspecified.AddHours(settings.OfficeStartHour);
            var officeEndLocalDateTime = dayLocalUnspecified.AddHours(settings.OfficeEndHour);

            var officeStartOffset = targetTimeZone.GetUtcOffset(officeStartLocalDateTime);
            var officeEndOffset = targetTimeZone.GetUtcOffset(officeEndLocalDateTime);

            var officeStartLocal = new DateTimeOffset(officeStartLocalDateTime, officeStartOffset);
            var officeEndLocal = new DateTimeOffset(officeEndLocalDateTime, officeEndOffset);

            var cursorLocal = officeStartLocal;
            if (preferredLocal > officeStartLocal)
                cursorLocal = preferredLocal;

            cursorLocal = RoundUpToSlot(cursorLocal, settings.SlotMinutes);

            while (cursorLocal.AddMinutes(settings.SlotMinutes) <= officeEndLocal)
            {
                var startUtc = cursorLocal.ToUniversalTime();
                var endUtc = cursorLocal.AddMinutes(settings.SlotMinutes).ToUniversalTime();

                var free = await IsFreeAsync(startUtc, endUtc, settings, ct);
                if (free)
                    return startUtc;

                cursorLocal = cursorLocal.AddMinutes(settings.SlotMinutes);
            }

            return null;
        }

        public async Task<bool> AcceptInviteAsync(string calendarEventId, ReminderAutomationSettings settings, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(calendarEventId))
                return false;

            var calendarId = string.IsNullOrWhiteSpace(settings.CalendarId) ? "primary" : settings.CalendarId;
            var service = await CreateCalendarClientAsync(ct);


            GoogleCalendarEvent existingEvent;
            try
            {
                existingEvent = await service.Events.Get(calendarId, calendarEventId).ExecuteAsync(ct);
            }
            catch
            {
                return false;
            }

            if (existingEvent == null || existingEvent.Attendees == null || existingEvent.Attendees.Count == 0)
                return false;

            var selfAttendee = existingEvent.Attendees.FirstOrDefault(x => x.Self == true);

            if (selfAttendee == null)
            {
                var activeEmail = await _db.EmailOAuthAccounts
                    .Where(x => x.Active && x.Provider == "Gmail")
                    .OrderByDescending(x => x.Id)
                    .Select(x => x.Email)
                    .FirstOrDefaultAsync(ct);

                if (!string.IsNullOrWhiteSpace(activeEmail))
                {
                    selfAttendee = existingEvent.Attendees.FirstOrDefault(x =>
                        x.Email != null &&
                        x.Email.Equals(activeEmail, StringComparison.OrdinalIgnoreCase));
                }
            }

            if (selfAttendee == null)
                return false;

            selfAttendee.ResponseStatus = "accepted";

            await service.Events.Update(existingEvent, calendarId, calendarEventId).ExecuteAsync(ct);
            return true;
        }

        public async Task<CalendarApprovalEventResult?> CreateApprovalSuggestionEventAsync(
     DateTimeOffset startUtc,
     DateTimeOffset endUtc,
     string title,
     string description,
     ReminderAutomationSettings settings,
     CancellationToken ct)
        {
            if (endUtc <= startUtc)
                return null;

            var calendarId = string.IsNullOrWhiteSpace(settings.CalendarId) ? "primary" : settings.CalendarId;
            var service = await CreateCalendarClientAsync(ct);

            var configuredTimeZoneId = string.IsNullOrWhiteSpace(settings.TimezoneId)
                ? "Asia/Karachi"
                : settings.TimezoneId;

            var targetTimeZone = AppTimeHelper.ResolveTimeZone(configuredTimeZoneId);

            // Convert UTC to local time for calendar event creation
            var localStart = TimeZoneInfo.ConvertTime(startUtc, targetTimeZone);
            var localEnd = TimeZoneInfo.ConvertTime(endUtc, targetTimeZone);

            var eventToCreate = new GoogleCalendarEvent
            {
                Summary = string.IsNullOrWhiteSpace(title) ? "Suggested meeting" : title,
                Description = description ?? "",
                Start = new global::Google.Apis.Calendar.v3.Data.EventDateTime
                {
                    DateTimeDateTimeOffset = localStart,
                    TimeZone = configuredTimeZoneId
                },
                End = new global::Google.Apis.Calendar.v3.Data.EventDateTime
                {
                    DateTimeDateTimeOffset = localEnd,
                    TimeZone = configuredTimeZoneId
                }
            };

            var createdEvent = await service.Events.Insert(eventToCreate, calendarId).ExecuteAsync(ct);

            if (createdEvent == null)
                return null;

            return new CalendarApprovalEventResult
            {
                EventId = createdEvent.Id ?? "",
                EventHtmlLink = createdEvent.HtmlLink ?? "",
                StartUtc = startUtc,
                EndUtc = endUtc
            };
        }

        public async Task<CalendarEventSnapshot?> GetEventSnapshotAsync(string calendarEventId, ReminderAutomationSettings settings, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(calendarEventId))
                return null;

            var calendarId = string.IsNullOrWhiteSpace(settings.CalendarId) ? "primary" : settings.CalendarId;
            var service = await CreateCalendarClientAsync(ct);

            GoogleCalendarEvent existingEvent;
            try
            {
                existingEvent = await service.Events.Get(calendarId, calendarEventId).ExecuteAsync(ct);
            }
            catch
            {
                return null;
            }

            if (existingEvent == null)
                return null;

            DateTimeOffset? startUtc = null;
            DateTimeOffset? endUtc = null;

            if (existingEvent.Start?.DateTimeDateTimeOffset != null)
                startUtc = existingEvent.Start.DateTimeDateTimeOffset.Value.ToUniversalTime();

            if (existingEvent.End?.DateTimeDateTimeOffset != null)
                endUtc = existingEvent.End.DateTimeDateTimeOffset.Value.ToUniversalTime();

            return new CalendarEventSnapshot
            {
                EventId = existingEvent.Id ?? "",
                HtmlLink = existingEvent.HtmlLink ?? "",
                StartUtc = startUtc,
                EndUtc = endUtc
            };
        }

        private static DateTimeOffset RoundUpToSlot(DateTimeOffset time, int slotMinutes)
        {
            if (slotMinutes <= 1)
                return time;

            var remainder = time.Minute % slotMinutes;
            if (remainder == 0)
                return time;

            var add = slotMinutes - remainder;
            return time.AddMinutes(add);
        }

        private async Task<CalendarService> CreateCalendarClientAsync(CancellationToken ct)
        {
            // Your token helper should create a Google credential with calendar scope already approved.
            var credential = await _tokenHelper.GetGoogleCredentialAsync(ct);

            return new CalendarService(new BaseClientService.Initializer
            {
                HttpClientInitializer = credential,
                ApplicationName = "SmartAssistant"
            });
        }
    }
}