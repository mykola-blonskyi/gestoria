using static System.FormattableString;

namespace GestorIA.Engine;

// End is the last day a filing is on time, already moved off non-working days.
public sealed record DueWindow(DateOnly Start, DateOnly End);

// The one place a configured window becomes the days a taxpayer resident in a region files within (#40). Local holidays
// also count (Ley 39/2015 art. 30.6) but the engine does not know the municipality.
public static class FilingDeadline
{
    public const string LocalHolidaysNotApplied =
        "municipal holidays where the taxpayer lives are not applied, so the date shown can be early but never late";

    public const string Rule =
        "a last day that is a Saturday, a Sunday or a national or regional holiday moves to the next working day "
            + "(Orden EHA/672/2007 art. 7 for Modelo 130; Ley 39/2015 art. 30.2, 30.5 and 30.6, supletoria under its DA 1ª.2.a and LGT art. 97.b); "
            + LocalHolidaysNotApplied;

    public static (DueWindow Window, TraceStep Step) Modelo130(Quarter quarter, string region, TaxYearConfig config) =>
        Resolve(
            config.Calendar.Modelo130[(int)quarter - 1],
            region,
            config,
            "m130.due-date",
            TraceSection.Modelo130,
            Invariant($"Último día para presentar el Modelo 130 del {quarter}"),
            "calendar.modelo130");

    public static (DueWindow Window, TraceStep Step) Renta(string region, TaxYearConfig config) =>
        Resolve(
            config.Calendar.Renta,
            region,
            config,
            "renta.due-date",
            TraceSection.Resultado,
            "Último día para presentar la declaración anual",
            "calendar.renta");

    private static (DueWindow Window, TraceStep Step) Resolve(
        CalendarWindow window, string region, TaxYearConfig config, string id, TraceSection section, string title, string calendarName)
    {
        var holidays = config.Calendar.Holidays
            .Concat(config.Regions.For(region).Holidays)
            .Select(day => day.In(config.TaxYear))
            .ToHashSet();
        var start = window.Start.In(config.TaxYear);
        var configuredEnd = window.End.In(config.TaxYear);
        var end = configuredEnd;
        var skipped = new List<string>();

        while (end.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday || holidays.Contains(end))
        {
            skipped.Add(Invariant($"{Iso(end)} {end.DayOfWeek}") + (holidays.Contains(end) ? ", holiday" : ""));
            end = end.AddDays(1);
        }

        var formula = skipped.Count == 0
            ? Invariant($"{Iso(configuredEnd)} {configuredEnd.DayOfWeek}, a working day → {Iso(end)}")
            : Invariant($"{string.Join("; ", skipped)} → {Iso(end)}");

        var step = new TraceStep(
            id,
            section,
            title,
            [new("start", Iso(start)), new("configuredEnd", Iso(configuredEnd)), new("region", region)],
            formula,
            new TraceValue.Date(end),
            Invariant($"config {calendarName}, calendar.holidays, regions.{region}.holidays; {Rule}"));

        return (new DueWindow(start, end), step);
    }

    private static string Iso(DateOnly day) => Invariant($"{day:yyyy-MM-dd}");
}
