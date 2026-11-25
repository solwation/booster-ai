using System.Globalization;

namespace BoosterAI;

/// <summary>
/// Human-readable times in the UI language (#208), for every user-facing time in BoosterAI: plan reset times
/// ("om 1 h 10 min (15:30)", "fredag 10:00"), read times ("nyss", "för 8 min sedan", "i går 18:10"), durations
/// ("1 h 15 min") and moments with their day ("i dag 14:20", "5 nov 10:00"). Pure: the caller passes <c>now</c>, and
/// <c>zone</c> (default the local time zone) decides the clock and which day is today. The texts come from Loc.
/// </summary>
public static class HumanTime
{
    /// <summary>A date this many days or more away also names its year.</summary>
    public const int YearAfterDays = 183;

    /// <summary>Whole minutes as "35 min", "2 h" or "1 h 15 min" (en "35m", "2h", "1h 15m"); negative counts as 0.</summary>
    public static string Minutes(long minutes)
    {
        minutes = Math.Max(0, minutes);
        if (minutes < 60) return Loc.Format("time.minutes", minutes);
        return minutes % 60 == 0 ? Loc.Format("time.hours", minutes / 60) : Loc.Format("time.hoursMinutes", minutes / 60, minutes % 60);
    }

    /// <summary>
    /// When something starts over, such as a plan window: within 24 hours a countdown with the clock ("om 1 h 10 min (15:30)"),
    /// otherwise the day and clock (<see cref="Day"/>: "i morgon 09:00", "fredag 10:00", "5 dec 10:00").
    /// </summary>
    public static string Until(DateTimeOffset time, DateTimeOffset now, TimeZoneInfo? zone = null)
    {
        var left = time - now;
        if (left <= TimeSpan.Zero || left > TimeSpan.FromHours(24)) return Day(time, now, zone);
        var minutes = Math.Max(1, (long)Math.Round(left.TotalMinutes, MidpointRounding.AwayFromZero));
        return Loc.Format("time.in", Minutes(minutes), Clock(time, zone));
    }

    /// <summary>
    /// When something was read or happened: "nyss" within a minute (also slightly ahead, for clock skew), "för 8 min sedan"
    /// within the hour, otherwise the day and clock (<see cref="Day"/>: "i dag 09:45", "i går 18:10", "fredag 10:00").
    /// </summary>
    public static string Since(DateTimeOffset time, DateTimeOffset now, TimeZoneInfo? zone = null)
    {
        var ago = now - time;
        if (ago.Duration() < TimeSpan.FromMinutes(1)) return Loc.Get("time.now");
        if (ago > TimeSpan.Zero && ago < TimeSpan.FromHours(1)) return Loc.Format("time.ago", Minutes((long)ago.TotalMinutes));
        return Day(time, now, zone);
    }

    /// <summary>
    /// A moment with its day: "i dag 14:20", "i morgon 09:00", "i går 23:40", the full weekday name within six days either way
    /// ("fredag 10:00"), otherwise the date ("5 dec 10:00", with the year from <see cref="YearAfterDays"/> days away).
    /// </summary>
    public static string Day(DateTimeOffset time, DateTimeOffset now, TimeZoneInfo? zone = null)
    {
        zone ??= TimeZoneInfo.Local;
        var local = TimeZoneInfo.ConvertTime(time, zone);
        var clock = local.ToString("HH:mm", CultureInfo.InvariantCulture);
        var days = (local.Date - TimeZoneInfo.ConvertTime(now, zone).Date).Days;
        return days switch
        {
            0 => Loc.Format("time.today", clock),
            1 => Loc.Format("time.tomorrow", clock),
            -1 => Loc.Format("time.yesterday", clock),
            >= -6 and <= 6 => Loc.Format("time.weekday", Loc.Get("time.day." + (int)local.DayOfWeek), clock),
            _ => Loc.Format(Math.Abs(days) >= YearAfterDays ? "time.date.year" : "time.date",
                local.Day, Loc.Get("time.month." + local.Month), clock, local.Year),
        };
    }

    /// <summary>The clock alone, "15:30", in the zone.</summary>
    public static string Clock(DateTimeOffset time, TimeZoneInfo? zone = null) =>
        TimeZoneInfo.ConvertTime(time, zone ?? TimeZoneInfo.Local).ToString("HH:mm", CultureInfo.InvariantCulture);
}
