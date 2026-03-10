using System;

namespace SmartAssistant.Api.Helpers
{
    public static class AppTimeHelper
    {
        public static TimeZoneInfo ResolveTimeZone(string? configuredTimeZoneId)
        {
            var requestedTimeZoneId = string.IsNullOrWhiteSpace(configuredTimeZoneId)
                ? "Asia/Karachi"
                : configuredTimeZoneId.Trim();

            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(requestedTimeZoneId);
            }
            catch
            {
            }

            var fallbackTimeZoneIds = new[]
            {
                "Asia/Karachi",
                "Pakistan Standard Time"
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

        public static string FormatUtcAsLocal(DateTimeOffset utcDateTime, string? timeZoneId, string format = "ddd, dd MMM yyyy  hh:mm tt")
        {
            var targetTimeZone = ResolveTimeZone(timeZoneId);
            var localDateTime = TimeZoneInfo.ConvertTime(utcDateTime, targetTimeZone);
            return localDateTime.ToString(format);
        }
    }
}