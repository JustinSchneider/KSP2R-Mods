using System;

namespace FlightPlan
{
    /// <summary>The one helper from Flight Plan's FPUtility that the vendored MechJeb code calls (for its debug logs).</summary>
    internal static class FPUtility
    {
        public static string SecondsToTimeString(double seconds, bool addSpacing = true, bool returnLastUnit = false, bool omitFractionalSeconds = false)
        {
            if (seconds == double.PositiveInfinity)
            {
                return "∞";
            }
            else if (seconds == double.NegativeInfinity)
            {
                return "-∞";
            }

            double _cap = Math.Floor(seconds);

            string _result = "";
            string _spacing = "";
            if (addSpacing)
            {
                _spacing = " ";
            }

            if (seconds < 0)
            {
                _result += "-";
                seconds = Math.Abs(seconds);
            }

            int _secPerYear = 21600 * 426 + 32 * 60;
            int _years = (int)(_cap / _secPerYear);
            int _days = (int)((_cap - (_years * _secPerYear)) / 21600);
            int _hours = (int)((_cap - (_days * 21600) - (_years * _secPerYear)) / 3600);
            int _minutes = (int)((_cap - (_hours * 3600) - (_days * 21600) - (_years * _secPerYear)) / 60);
            double _secs = (seconds - (_years * _secPerYear) - (_days * 21600) - (_hours * 3600) - (_minutes * 60));

            if (_years > 0)
            {
                _result += $"{_years}{_spacing}y ";
            }

            if (_days > 0)
            {
                _result += $"{_days}{_spacing}d ";
            }

            if (_hours > 0 || _days > 0)
            {
                {
                    _result += $"{_hours}:";
                }
            }

            if (_minutes > 0 || _hours > 0 || _days > 0)
            {
                if (_hours > 0 || _days > 0)
                {
                    _result += $"{_minutes:00.}:";
                }
                else
                {
                    _result += $"{_minutes}:";
                }
            }

            if (_minutes > 0 || _hours > 0 || _days > 0)
            {
                if (omitFractionalSeconds)
                    _result += returnLastUnit ? $"{_secs:00}{_spacing}" : $"{_secs:00}";
                else
                    _result += returnLastUnit ? $"{_secs:00.00}{_spacing}" : $"{_secs:00.00}";
            }
            else
            {
                if (omitFractionalSeconds)
                    _result += returnLastUnit ? $"{_secs:00}{_spacing}" : $"{_secs:00}";
                else
                    _result += returnLastUnit ? $"{_secs:00.00}{_spacing}" : $"{_secs:00.00}";
            }

            return _result;
        }
    }
}
