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
            var calendarId = string.IsNullOrWhiteSpace(settings.CalendarId) ? "primary" : settings.CalendarId;

            var service = await CreateCalendarClientAsync(ct);

            var startUtc = reminder.ReminderTime.ToUniversalTime();

            var durationMinutes = settings.SlotMinutes > 0 ? settings.SlotMinutes : 60;

            var endUtc = startUtc.AddMinutes(durationMinutes);

            var googleEvent = new GoogleCalendarEvent
            {
                Summary = reminder.Title ?? "Reminder",
                Description = reminder.Description ?? "",

                Start = new global::Google.Apis.Calendar.v3.Data.EventDateTime
                {
                    DateTime = startUtc.UtcDateTime,
                    TimeZone = "UTC"
                },

                End = new global::Google.Apis.Calendar.v3.Data.EventDateTime
                {
                    DateTime = endUtc.UtcDateTime,
                    TimeZone = "UTC"
                }
            };

            var created = await service.Events.Insert(googleEvent, calendarId).ExecuteAsync(ct);

            return created.Id;
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
            request.MaxResults = 50;

            var events = await request.ExecuteAsync(ct);

            if (events?.Items == null || events.Items.Count == 0)
                return true;

            foreach (var calendarEvent in events.Items)
            {
                if (!DoesEventOverlap(calendarEvent, startUtc, endUtc))
                    continue;

                if (!IsBlockingCalendarEvent(calendarEvent))
                    continue;

                return false;
            }

            return true;
        }

        public async Task<DateTimeOffset?> FindNextFreeSlotAsync(DateTimeOffset fromUtc, ReminderAutomationSettings settings, CancellationToken ct)
        {
            var targetTimeZone = AppTimeHelper.ResolveTimeZone(settings.TimezoneId);

            var fromLocal = AppTimeHelper.ConvertUtcToLocal(fromUtc, settings.TimezoneId);

            for (int dayOffset = 0; dayOffset < 7; dayOffset++)
            {
                var dayLocalDate = fromLocal.Date.AddDays(dayOffset);

                var dayLocalUnspecified = DateTime.SpecifyKind(dayLocalDate.Date, DateTimeKind.Unspecified);

                var officeStartLocalDateTime = dayLocalUnspecified.AddHours(settings.OfficeStartHour);
                var officeEndLocalDateTime = dayLocalUnspecified.AddHours(settings.OfficeEndHour);

                var officeStartOffset = targetTimeZone.GetUtcOffset(officeStartLocalDateTime);
                var officeEndOffset = targetTimeZone.GetUtcOffset(officeEndLocalDateTime);

                var officeStartLocal = new DateTimeOffset(officeStartLocalDateTime, officeStartOffset);
                var officeEndLocal = new DateTimeOffset(officeEndLocalDateTime, officeEndOffset);

                var cursorLocal = officeStartLocal;

                if (dayOffset == 0 && fromLocal > officeStartLocal)
                    cursorLocal = fromLocal;

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
            var preferredLocal = AppTimeHelper.ConvertUtcToLocal(preferredStartUtc, settings.TimezoneId);

            var dayLocalDate = preferredLocal.Date;
            var dayLocalUnspecified = DateTime.SpecifyKind(dayLocalDate.Date, DateTimeKind.Unspecified);

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

            // IMPORTANT:
            // Always send UTC to Google Calendar.
            // This avoids double timezone interpretation.
            var eventToCreate = new GoogleCalendarEvent
            {
                Summary = string.IsNullOrWhiteSpace(title) ? "Suggested meeting slot" : title,
                Description = description ?? "",
                Start = BuildGoogleUtcEventDateTime(startUtc),
                End = BuildGoogleUtcEventDateTime(endUtc),
                Transparency = "transparent"
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
        public async Task<bool> DeleteEventAsync(string calendarEventId, ReminderAutomationSettings settings, CancellationToken ct)
         {
            if (string.IsNullOrWhiteSpace(calendarEventId))
                return false;

            var calendarId = string.IsNullOrWhiteSpace(settings.CalendarId) ? "primary" : settings.CalendarId;
            var service = await CreateCalendarClientAsync(ct);

            try
            {
                await service.Events.Delete(calendarId, calendarEventId).ExecuteAsync(ct);
                return true;
            }
            catch (global::Google.GoogleApiException ex)
            {
                // If event is already gone, treat it as success so the flow remains idempotent.
                if (ex.HttpStatusCode == System.Net.HttpStatusCode.NotFound)
                    return true;

                return false;
            }
            catch
            {
                return false;
            }
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
        private static global::Google.Apis.Calendar.v3.Data.EventDateTime BuildGoogleUtcEventDateTime(DateTimeOffset utcDateTime)
        {
            var normalizedUtc = utcDateTime.ToUniversalTime();

            return new global::Google.Apis.Calendar.v3.Data.EventDateTime
            {
                // Send clean UTC time to Google Calendar
                DateTime = normalizedUtc.UtcDateTime,
                TimeZone = "UTC"
            };
        }
        private static bool DoesEventOverlap(GoogleCalendarEvent calendarEvent, DateTimeOffset startUtc, DateTimeOffset endUtc)
        {
            var eventStartUtc = GetEventStartUtc(calendarEvent);
            var eventEndUtc = GetEventEndUtc(calendarEvent);

            if (!eventStartUtc.HasValue || !eventEndUtc.HasValue)
                return false;

            return eventStartUtc.Value < endUtc && eventEndUtc.Value > startUtc;
        }

        private static DateTimeOffset? GetEventStartUtc(GoogleCalendarEvent calendarEvent)
        {
            if (calendarEvent.Start == null)
                return null;

            if (calendarEvent.Start.DateTimeDateTimeOffset.HasValue)
                return calendarEvent.Start.DateTimeDateTimeOffset.Value.ToUniversalTime();

            if (!string.IsNullOrWhiteSpace(calendarEvent.Start.Date) &&
                DateTimeOffset.TryParse(calendarEvent.Start.Date, out var allDayDate))
            {
                return allDayDate.ToUniversalTime();
            }

            return null;
        }

        private static DateTimeOffset? GetEventEndUtc(GoogleCalendarEvent calendarEvent)
        {
            if (calendarEvent.End == null)
                return null;

            if (calendarEvent.End.DateTimeDateTimeOffset.HasValue)
                return calendarEvent.End.DateTimeDateTimeOffset.Value.ToUniversalTime();

            if (!string.IsNullOrWhiteSpace(calendarEvent.End.Date) &&
                DateTimeOffset.TryParse(calendarEvent.End.Date, out var allDayDate))
            {
                return allDayDate.ToUniversalTime();
            }

            return null;
        }

        private static bool IsBlockingCalendarEvent(GoogleCalendarEvent calendarEvent)
        {
            if (calendarEvent == null)
                return false;

            if (string.Equals(calendarEvent.Status, "cancelled", StringComparison.OrdinalIgnoreCase))
                return false;

            if (string.Equals(calendarEvent.Transparency, "transparent", StringComparison.OrdinalIgnoreCase))
                return false;

            var summary = calendarEvent.Summary ?? "";

            if (summary.Contains("Suggested meeting slot", StringComparison.OrdinalIgnoreCase))
                return false;

            if (calendarEvent.Attendees != null && calendarEvent.Attendees.Count > 0)
            {
                var selfAttendee = calendarEvent.Attendees.FirstOrDefault(attendee => attendee.Self == true);

                if (selfAttendee != null)
                {
                    var responseStatus = (selfAttendee.ResponseStatus ?? "").Trim().ToLowerInvariant();

                    if (responseStatus == "accepted")
                        return true;

                    if (responseStatus == "needsaction")
                        return false;

                    if (responseStatus == "tentative")
                        return false;

                    if (responseStatus == "declined")
                        return false;
                }
            }

            return true;
        }
    } }
