using BoosterAI;
using Xunit;

namespace BoosterAI.Tests;

/// <summary>
/// #208: every user-facing time is human-readable in Swedish and English. Fixed zones (never the machine's) keep the
/// results the same in any local time zone; Loc is global, so the tests share the language-switching collection.
/// </summary>
[Collection(ConfigFileCollection.Name)]
public class HumanTimeTests
{
    // UTC+1 without daylight saving: 2026-11-25 10:00 is a Wednesday.
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.CreateCustomTimeZone("Test+1", TimeSpan.FromHours(1), "Test+1", "Test+1");
    private static readonly DateTimeOffset Now = new(2026, 11, 25, 10, 0, 0, TimeSpan.FromHours(1));

    private static void In(string language, Action check)
    {
        var previous = Loc.Language;
        Loc.Use(language);
        try { check(); }
        finally { Loc.Use(previous); }
    }

    [Theory]
    [InlineData("sv", 0, "0 min")]
    [InlineData("sv", 1, "1 min")]
    [InlineData("sv", 59, "59 min")]
    [InlineData("sv", 60, "1 h")]
    [InlineData("sv", 75, "1 h 15 min")]
    [InlineData("sv", 1500, "25 h")]
    [InlineData("sv", -3, "0 min")]
    [InlineData("en", 0, "0m")]
    [InlineData("en", 59, "59m")]
    [InlineData("en", 60, "1h")]
    [InlineData("en", 75, "1h 15m")]
    public void DurationsUseHoursAndMinutesAbove59Minutes(string language, long minutes, string expected) =>
        In(language, () => Assert.Equal(expected, HumanTime.Minutes(minutes)));

    [Theory]
    // Within 24 hours: a countdown with the clock; the minutes are rounded and never 0.
    [InlineData("sv", 30, "om 1 min (10:00)")]
    [InlineData("sv", 35 * 60, "om 35 min (10:35)")]
    [InlineData("sv", 70 * 60, "om 1 h 10 min (11:10)")]
    [InlineData("sv", 2 * 3600, "om 2 h (12:00)")]
    [InlineData("sv", 24 * 3600, "om 24 h (10:00)")]
    [InlineData("en", 35 * 60, "in 35m (10:35)")]
    [InlineData("en", 70 * 60, "in 1h 10m (11:10)")]
    [InlineData("en", 24 * 3600, "in 24h (10:00)")]
    // Later: tomorrow, the full weekday name for the next days, then a readable date (with the year far away).
    [InlineData("sv", 25 * 3600, "i morgon 11:00")]
    [InlineData("sv", 2 * 86400, "fredag 10:00")]
    [InlineData("sv", 6 * 86400, "tisdag 10:00")]
    [InlineData("sv", 7 * 86400, "2 dec 10:00")]
    [InlineData("sv", 10 * 86400, "5 dec 10:00")]
    [InlineData("sv", 200 * 86400, "13 juni 2027 10:00")]
    [InlineData("en", 25 * 3600, "tomorrow 11:00")]
    [InlineData("en", 2 * 86400, "Friday 10:00")]
    [InlineData("en", 6 * 86400, "Tuesday 10:00")]
    [InlineData("en", 10 * 86400, "Dec 5 10:00")]
    [InlineData("en", 200 * 86400, "Jun 13, 2027 10:00")]
    public void ResetTimesCountDownWithinADayThenNameTheDay(string language, int seconds, string expected) =>
        In(language, () => Assert.Equal(expected, HumanTime.Until(Now.AddSeconds(seconds), Now, Zone)));

    [Theory]
    [InlineData("sv", -20, "nyss")]
    [InlineData("sv", 20, "nyss")] // slightly ahead (clock skew) still reads "just now"
    [InlineData("sv", -8 * 60, "för 8 min sedan")]
    [InlineData("sv", -59 * 60 - 30, "för 59 min sedan")]
    [InlineData("sv", -3600, "i dag 09:00")]
    [InlineData("sv", -10 * 3600, "i dag 00:00")]
    [InlineData("sv", -11 * 3600 - 50 * 60, "i går 22:10")]
    [InlineData("sv", -2 * 86400, "måndag 10:00")]
    [InlineData("sv", -6 * 86400, "torsdag 10:00")]
    [InlineData("sv", -10 * 86400, "15 nov 10:00")]
    [InlineData("sv", -300 * 86400, "29 jan 2026 10:00")]
    [InlineData("sv", 2 * 3600, "i dag 12:00")] // far ahead: the moment, not "nyss"
    [InlineData("en", -20, "just now")]
    [InlineData("en", -8 * 60, "8m ago")]
    [InlineData("en", -75 * 60, "today 08:45")]
    [InlineData("en", -11 * 3600 - 50 * 60, "yesterday 22:10")]
    [InlineData("en", -2 * 86400, "Monday 10:00")]
    [InlineData("en", -10 * 86400, "Nov 15 10:00")]
    [InlineData("en", -300 * 86400, "Jan 29, 2026 10:00")]
    public void ReadTimesSayJustNowMinutesAgoThenTheDay(string language, int seconds, string expected) =>
        In(language, () => Assert.Equal(expected, HumanTime.Since(Now.AddSeconds(seconds), Now, Zone)));

    [Theory]
    [InlineData("sv", 0, "i dag 10:00")]
    [InlineData("sv", -10 * 3600 - 20 * 60, "i går 23:40")]
    [InlineData("sv", -5 * 86400, "fredag 10:00")]
    [InlineData("en", 0, "today 10:00")]
    [InlineData("en", -10 * 3600 - 20 * 60, "yesterday 23:40")]
    [InlineData("en", -5 * 86400, "Friday 10:00")]
    public void MomentsNameTheirDay(string language, int seconds, string expected) =>
        In(language, () => Assert.Equal(expected, HumanTime.Day(Now.AddSeconds(seconds), Now, Zone)));

    [Fact]
    public void TheZoneDecidesTheClockAndWhichDayIsToday()
    {
        var now = new DateTimeOffset(2026, 11, 25, 2, 0, 0, TimeSpan.Zero);
        var time = new DateTimeOffset(2026, 11, 25, 0, 30, 0, TimeSpan.Zero);
        var west = TimeZoneInfo.CreateCustomTimeZone("Test-2", TimeSpan.FromHours(-2), "Test-2", "Test-2");
        In("sv", () =>
        {
            Assert.Equal("i dag 01:30", HumanTime.Day(time, now, Zone));
            Assert.Equal("i går 22:30", HumanTime.Day(time, now, west));
            Assert.Equal("om 1 h 30 min (04:30)", HumanTime.Until(now.AddMinutes(90), now, Zone));
            Assert.Equal("om 1 h 30 min (01:30)", HumanTime.Until(now.AddMinutes(90), now, west));
        });
    }

    [Fact]
    public void EveryWeekdayAndMonthHasANameInBothLanguages() => In("sv", () =>
    {
        foreach (var language in Loc.Languages)
        {
            var table = Loc.Table(language);
            for (var d = 0; d < 7; d++) Assert.False(string.IsNullOrWhiteSpace(table["time.day." + d]));
            for (var m = 1; m <= 12; m++) Assert.False(string.IsNullOrWhiteSpace(table["time.month." + m]));
        }
    });
}
