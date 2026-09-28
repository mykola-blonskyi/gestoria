namespace GestorIA.Api;

// The taxpayer's own date: every region GestorIA covers is on Madrid time (ADR-0016), so "today" and "the day of" an instant
// are Madrid's date, which differs from UTC's between midnight and 01:00 or 02:00.
public static class MadridDay
{
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Madrid");

    public static DateOnly Of(DateTimeOffset instant) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, Zone).DateTime);
}
