using System;
using System.Collections.Generic;

namespace TripFund.App.Utilities
{
    public static class TimeZoneMapper
    {
        private static readonly Dictionary<string, string> IanaToItalianCity = new(StringComparer.OrdinalIgnoreCase)
        {
            { "Europe/Rome", "Roma" },
            { "Europe/Paris", "Parigi" },
            { "Europe/London", "Londra" },
            { "Europe/Berlin", "Berlino" },
            { "Europe/Madrid", "Madrid" },
            { "Europe/Amsterdam", "Amsterdam" },
            { "Europe/Brussels", "Bruxelles" },
            { "Europe/Vienna", "Vienna" },
            { "Europe/Zurich", "Zurigo" },
            { "Europe/Athens", "Atene" },
            { "Europe/Lisbon", "Lisbona" },
            { "Europe/Dublin", "Dublino" },
            { "Europe/Stockholm", "Stoccolma" },
            { "Europe/Oslo", "Oslo" },
            { "Europe/Copenhagen", "Copenaghen" },
            { "Europe/Helsinki", "Helsinki" },
            { "Europe/Warsaw", "Varsavia" },
            { "Europe/Prague", "Praga" },
            { "Europe/Budapest", "Budapest" },
            { "Europe/Bucharest", "Bucarest" },
            { "Europe/Sofia", "Sofia" },
            { "Europe/Istanbul", "Istanbul" },
            { "Europe/Moscow", "Mosca" },
            { "Europe/Kiev", "Kiev" },
            { "Europe/Belgrade", "Belgrado" },
            { "Europe/Zagreb", "Zagabria" },
            { "Europe/Ljubljana", "Lubiana" },
            { "Europe/Bratislava", "Bratislava" },
            { "Europe/Tallinn", "Tallinn" },
            { "Europe/Riga", "Riga" },
            { "Europe/Vilnius", "Vilnius" },
            { "Europe/Luxembourg", "Lussemburgo" },
            { "Europe/Monaco", "Monaco" },
            { "Europe/Malta", "Malta" },
            { "Europe/Andorra", "Andorra" },
            { "Europe/Gibraltar", "Gibilterra" },
            { "Europe/Vatican", "Città del Vaticano" },
            { "Europe/San_Marino", "San Marino" },
            { "America/New_York", "New York" },
            { "America/Los_Angeles", "Los Angeles" },
            { "America/Chicago", "Chicago" },
            { "America/Denver", "Denver" },
            { "America/Phoenix", "Phoenix" },
            { "America/Anchorage", "Anchorage" },
            { "America/Honolulu", "Honolulu" },
            { "America/Mexico_City", "Città del Messico" },
            { "America/Toronto", "Toronto" },
            { "America/Vancouver", "Vancouver" },
            { "America/Montreal", "Montreal" },
            { "America/Sao_Paulo", "San Paolo" },
            { "America/Argentina/Buenos_Aires", "Buenos Aires" },
            { "America/Santiago", "Santiago" },
            { "America/Bogota", "Bogotà" },
            { "America/Lima", "Lima" },
            { "America/Caracas", "Caracas" },
            { "America/Havana", "L'Avana" },
            { "America/Costa_Rica", "Costa Rica" },
            { "America/Panama", "Panama" },
            { "America/Santo_Domingo", "Santo Domingo" },
            { "America/Puerto_Rico", "Porto Rico" },
            { "Asia/Tokyo", "Tokyo" },
            { "Asia/Shanghai", "Shanghai" },
            { "Asia/Hong_Kong", "Hong Kong" },
            { "Asia/Singapore", "Singapore" },
            { "Asia/Dubai", "Dubai" },
            { "Asia/Seoul", "Seoul" },
            { "Asia/Bangkok", "Bangkok" },
            { "Asia/Jakarta", "Giacarta" },
            { "Asia/Manila", "Manila" },
            { "Asia/Kolkata", "Calcutta" },
            { "Asia/Jerusalem", "Gerusalemme" },
            { "Asia/Tehran", "Teheran" },
            { "Asia/Baghdad", "Baghdad" },
            { "Asia/Riyadh", "Riad" },
            { "Asia/Taipei", "Taipei" },
            { "Asia/Ho_Chi_Minh", "Ho Chi Minh" },
            { "Asia/Kathmandu", "Kathmandu" },
            { "Asia/Ulaanbaatar", "Ulan Bator" },
            { "Australia/Sydney", "Sydney" },
            { "Australia/Melbourne", "Melbourne" },
            { "Australia/Perth", "Perth" },
            { "Australia/Brisbane", "Brisbane" },
            { "Australia/Adelaide", "Adelaide" },
            { "Australia/Darwin", "Darwin" },
            { "Australia/Hobart", "Hobart" },
            { "Pacific/Auckland", "Auckland" },
            { "Pacific/Fiji", "Fiji" },
            { "Pacific/Guam", "Guam" },
            { "Pacific/Honolulu", "Honolulu" },
            { "Pacific/Tahiti", "Tahiti" },
            { "Africa/Cairo", "Cairo" },
            { "Africa/Johannesburg", "Johannesburg" },
            { "Africa/Nairobi", "Nairobi" },
            { "Africa/Casablanca", "Casablanca" },
            { "Africa/Lagos", "Lagos" },
            { "Africa/Algiers", "Algeri" },
            { "Africa/Tunis", "Tunisi" },
            { "Africa/Dakar", "Dakar" },
            { "Africa/Addis_Ababa", "Addis Abeba" },
            { "Indian/Maldives", "Maldive" },
            { "Indian/Mauritius", "Mauritius" },
            { "UTC", "UTC" }
        };

        public static string GetItalianCityName(string ianaId)
        {
            if (IanaToItalianCity.TryGetValue(ianaId, out var cityName))
            {
                return cityName;
            }
            
            // Fallback: try to extract the city part from "Region/City"
            var parts = ianaId.Split('/');
            if (parts.Length > 1)
            {
                return parts[parts.Length - 1].Replace('_', ' ');
            }
            
            return ianaId;
        }

        public static bool IsSupported(string ianaId)
        {
            return IanaToItalianCity.ContainsKey(ianaId);
        }

        private static List<TimeZoneInfo>? _cachedSupportedTimeZones;

        public static Task PreloadAsync()
        {
            return Task.Run(() =>
            {
                if (_cachedSupportedTimeZones == null)
                {
                    GetSupportedTimeZones();
                }
            });
        }

        public static List<TimeZoneInfo> GetSupportedTimeZones()
        {
            if (_cachedSupportedTimeZones != null) return _cachedSupportedTimeZones;

            _cachedSupportedTimeZones = TimeZoneInfo.GetSystemTimeZones()
                .Where(tz => IsSupported(tz.Id))
                .OrderBy(tz => tz.BaseUtcOffset)
                .ToList();

            return _cachedSupportedTimeZones;
        }

        public static IEnumerable<string> GetSupportedIanaIds()
        {
            return IanaToItalianCity.Keys;
        }

        public static string GetFormattedOffset(TimeZoneInfo tz, DateTimeOffset date)
        {
            var offset = tz.GetUtcOffset(date);
            
            if (offset == TimeSpan.Zero)
            {
                return "(UTC)";
            }
            
            var sign = offset >= TimeSpan.Zero ? "+" : "-";
            return $"(UTC{sign}{Math.Abs(offset.Hours):D2}:{Math.Abs(offset.Minutes):D2})";
        }
    }
}
