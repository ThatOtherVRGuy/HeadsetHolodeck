using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace SpeechIntent
{
    public static class DistanceUnitParser
    {
        public const string DistancePattern =
            @"(?<value>\d+(\.\d+)?|one|two|three|four|five|six|seven|eight|nine|ten)\s*(?<unit>millimeters?|millimetres?|mm|centimeters?|centimetres?|cm|kilometers?|kilometres?|km|meters?|metres?|m|inches|inch|in|feet|foot|ft|yards?|yd|miles?|mi)\b";

        public static bool TryExtractMeters(string text, out float meters)
        {
            meters = 0f;
            Match match = Regex.Match(text ?? string.Empty, DistancePattern, RegexOptions.IgnoreCase);
            if (!match.Success)
                return false;

            return TryConvertToMeters(match.Groups["value"].Value, match.Groups["unit"].Value, out meters);
        }

        public static string RemoveDistances(string text)
        {
            return Regex.Replace(text ?? string.Empty, DistancePattern, "", RegexOptions.IgnoreCase).Trim();
        }

        public static bool TryConvertToMeters(string valueText, string unitText, out float meters)
        {
            meters = 0f;
            if (!TryParseValue(valueText, out float value))
                return false;

            meters = value * UnitToMeters(unitText);
            return meters > 0f;
        }

        static bool TryParseValue(string valueText, out float value)
        {
            if (float.TryParse(valueText, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                return true;

            switch ((valueText ?? "").Trim().ToLowerInvariant())
            {
                case "one":
                    value = 1f;
                    return true;
                case "two":
                    value = 2f;
                    return true;
                case "three":
                    value = 3f;
                    return true;
                case "four":
                    value = 4f;
                    return true;
                case "five":
                    value = 5f;
                    return true;
                case "six":
                    value = 6f;
                    return true;
                case "seven":
                    value = 7f;
                    return true;
                case "eight":
                    value = 8f;
                    return true;
                case "nine":
                    value = 9f;
                    return true;
                case "ten":
                    value = 10f;
                    return true;
                default:
                    value = 0f;
                    return false;
            }
        }

        static float UnitToMeters(string unitText)
        {
            string unit = (unitText ?? "").Trim().ToLowerInvariant();
            switch (unit)
            {
                case "millimeter":
                case "millimeters":
                case "millimetre":
                case "millimetres":
                case "mm":
                    return 0.001f;

                case "centimeter":
                case "centimeters":
                case "centimetre":
                case "centimetres":
                case "cm":
                    return 0.01f;

                case "kilometer":
                case "kilometers":
                case "kilometre":
                case "kilometres":
                case "km":
                    return 1000f;

                case "inch":
                case "inches":
                case "in":
                    return 0.0254f;

                case "foot":
                case "feet":
                case "ft":
                    return 0.3048f;

                case "yard":
                case "yards":
                case "yd":
                    return 0.9144f;

                case "mile":
                case "miles":
                case "mi":
                    return 1609.344f;

                case "meter":
                case "meters":
                case "metre":
                case "metres":
                case "m":
                default:
                    return 1f;
            }
        }
    }
}
