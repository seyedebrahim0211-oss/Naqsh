using System.Globalization;

namespace Naqsh.Localization;

/// <summary>A date in the Solar Hijri (Jalali) calendar.</summary>
public readonly record struct JalaliDate(int Year, int Month, int Day);

/// <summary>Clock and date text for the header and outputs, in Jalali or Gregorian, with selected digits.</summary>
public static class DateFormatter
{
    private static readonly PersianCalendar Persian = new();

    public static JalaliDate ToJalali(DateTime date) =>
        new(Persian.GetYear(date), Persian.GetMonth(date), Persian.GetDayOfMonth(date));

    public static DateTime FromJalali(JalaliDate date) =>
        Persian.ToDateTime(date.Year, date.Month, date.Day, 0, 0, 0, 0);

    /// <summary>Long date, e.g. "شنبه ۱۰ آبان ۱۴۰۵" or "Saturday 1 November 2026".</summary>
    public static string FormatDate(DateTime date, LocalizationService loc)
    {
        ArgumentNullException.ThrowIfNull(loc);
        int y, m, d;
        string monthKey;
        if (loc.UseJalali)
        {
            var j = ToJalali(date);
            (y, m, d) = (j.Year, j.Month, j.Day);
            monthKey = "Month_Jalali_";
        }
        else
        {
            (y, m, d) = (date.Year, date.Month, date.Day);
            monthKey = "Month_Gregorian_";
        }

        var pattern = loc.Get("Date_Format");
        var text = pattern
            .Replace("{weekday}", loc.Get("Weekday_" + ((int)date.DayOfWeek).ToString(CultureInfo.InvariantCulture)), StringComparison.Ordinal)
            .Replace("{day}", d.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{month}", loc.Get(monthKey + m.ToString(CultureInfo.InvariantCulture)), StringComparison.Ordinal)
            .Replace("{year}", y.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
        return loc.Digits(text);
    }

    /// <summary>Clock text "HH:mm:ss" (24 h) or "h:mm:ss AM" (12 h), with selected digits.</summary>
    public static string FormatTime(DateTime time, LocalizationService loc)
    {
        ArgumentNullException.ThrowIfNull(loc);
        string text;
        if (loc.Clock24h)
        {
            text = time.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        }
        else
        {
            var suffix = loc.Get(time.Hour < 12 ? "Clock_AM" : "Clock_PM");
            text = time.ToString("h:mm:ss", CultureInfo.InvariantCulture) + " " + suffix;
        }

        return loc.Digits(text);
    }
}
