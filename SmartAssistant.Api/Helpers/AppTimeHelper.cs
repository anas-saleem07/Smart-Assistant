using System;
using System.Collections.Generic;

namespace SmartAssistant.Api.Helpers
{
    public static class AppTimeHelper
    {
        private static readonly Dictionary<string, string> WindowsToIanaMap =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "Pakistan Standard Time", "Asia/Karachi" },
                { "UTC", "UTC" }
            };

        private static readonly Dictionary<string, string> IanaToWindowsMap =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "Asia/Karachi", "Pakistan Standard Time" },
                { "UTC", "UTC" }
            };

        public static TimeZoneInfo ResolveTimeZone(string? configuredTimeZoneId)
        {
            var requestedTimeZoneId = string.IsNullOrWhiteSpace(configuredTimeZoneId)
                ? "Asia/Karachi"
                : configuredTimeZoneId.Trim();

            // 1) Try exact id first
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(requestedTimeZoneId);
            }
            catch
            {
            }

            // 2) Try mapped equivalent
            if (WindowsToIanaMap.TryGetValue(requestedTimeZoneId, out var mappedIanaId))
            {
                try
                {
                    return TimeZoneInfo.FindSystemTimeZoneById(mappedIanaId);
                }
                catch
                {
                }
            }

            if (IanaToWindowsMap.TryGetValue(requestedTimeZoneId, out var mappedWindowsId))
            {
                try
                {
                    return TimeZoneInfo.FindSystemTimeZoneById(mappedWindowsId);
                }
                catch
                {
                }
            }

            // 3) Safe fallback order
            var fallbackTimeZoneIds = new[]
            {
                "Asia/Karachi",
                "Pakistan Standard Time",
                "UTC"
            };

            for (int index = 0; index < fallbackTimeZoneIds.Length; index++)
            {
                try
                {
                    return TimeZoneInfo.FindSystemTimeZoneById(fallbackTimeZoneIds[index]);
                }
                catch
                {
                }
            }

            return TimeZoneInfo.Utc;
        }

        public static DateTimeOffset ConvertLocalDateTimeToUtc(DateTime localDateTime, string? timeZoneId)
        {
            var targetTimeZone = ResolveTimeZone(timeZoneId);

            // Important:
            // datetime-local from UI has no timezone.
            // We must treat it as wall-clock time in the configured office timezone.
            var unspecifiedLocalDateTime = DateTime.SpecifyKind(localDateTime, DateTimeKind.Unspecified);

            var localOffset = targetTimeZone.GetUtcOffset(unspecifiedLocalDateTime);
            var localDateTimeOffset = new DateTimeOffset(unspecifiedLocalDateTime, localOffset);

            return localDateTimeOffset.ToUniversalTime();
        }

        public static DateTimeOffset ConvertUtcToLocal(DateTimeOffset utcDateTime, string? timeZoneId)
        {
            var targetTimeZone = ResolveTimeZone(timeZoneId);
            return TimeZoneInfo.ConvertTime(utcDateTime, targetTimeZone);
        }

        public static DateTime ConvertUtcToLocalDateTime(DateTimeOffset utcDateTime, string? timeZoneId)
        {
            var localDateTimeOffset = ConvertUtcToLocal(utcDateTime, timeZoneId);
            return localDateTimeOffset.DateTime;
        }

        public static string FormatUtcAsLocal(
            DateTimeOffset utcDateTime,
            string? timeZoneId,
            string format = "ddd, dd MMM yyyy hh:mm tt")
        {
            var targetTimeZone = ResolveTimeZone(timeZoneId);
            var localDateTime = TimeZoneInfo.ConvertTime(utcDateTime, targetTimeZone);
            return localDateTime.ToString(format);
        }

        public static DateTimeOffset BuildUtcFromLocalParts(
            int year,
            int month,
            int day,
            int hour,
            int minute,
            string? timeZoneId)
        {
            var targetTimeZone = ResolveTimeZone(timeZoneId);

            var localDateTime = new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Unspecified);
            var localOffset = targetTimeZone.GetUtcOffset(localDateTime);

            return new DateTimeOffset(localDateTime, localOffset).ToUniversalTime();
        }
    }
}