using Google.Apis.Calendar.v3;
using Google.Apis.Services;
using SmartAssistant.Api.Data;
using SmartAssistant.Api.Services.Google;
using SmartAssistant.Core.Entities;
using System;
using System.Threading;
using System.Threading.Tasks;

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
            var tz = TimeZoneInfo.FindSystemTimeZoneById(settings.TimezoneId);

            // Convert to local time in selected timezone
            var fromLocal = TimeZoneInfo.ConvertTime(fromUtc, tz);

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

                var officeStartOffset = tz.GetUtcOffset(officeStartLocalDateTime);
                var officeEndOffset = tz.GetUtcOffset(officeEndLocalDateTime);

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