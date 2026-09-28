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

    // MonthlyCuotaSs moves the opposite way (backward, RD 1415/2004 art. 8.b)): a municipal holiday the engine does not know
    // about (Ley 39/2015 art. 30.6) can make the real last day one working day earlier than the one shown, never later.
    public const string LocalHolidaysNotAppliedBackward =
        "municipal holidays where the taxpayer lives are not applied, so the date shown may already be a working day late: pay a day earlier to be safe";

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
            "calendar.modelo130",
            Invariant($"calendar.modelo130 {quarter}"));

    public static (DueWindow Window, TraceStep Step) Renta(string region, TaxYearConfig config) =>
        Resolve(
            config.Calendar.Renta,
            region,
            config,
            "renta.due-date",
            TraceSection.Resultado,
            "Último día para presentar la declaración anual",
            "calendar.renta",
            "calendar.renta");

    // Modelo 303 (IVA) shares calendar.modelo130's windows: Reglamento del IVA (RD 1624/1992) art. 71.4 sets the same 1–20
    // (1–30 for the fourth period) days after the quarter. Modelo 349 filed quarterly has the same plazo, Orden EHA/769/2010
    // art. 10.2; above modelo349.quarterlyFilingCap it is monthly (art. 10.1), which needs intra-EU volumes the engine does
    // not have yet, so only the quarterly case is computed. Neither 303 nor 349 has a calculator (#70), so only the window is
    // asked for here.
    public static (DueWindow Window, TraceStep Step) SharedQuarterlyWindow(Quarter quarter, string region, TaxYearConfig config) =>
        Resolve(
            config.Calendar.Modelo130[(int)quarter - 1],
            region,
            config,
            "shared-quarterly.due-date",
            TraceSection.Modelo130,
            Invariant($"Último día del plazo trimestral compartido con el IVA del {quarter}"),
            "calendar.modelo130",
            Invariant($"calendar.modelo130 {quarter}"));

    // RD 1415/2004 (Reglamento General de Recaudación de la Seguridad Social) art. 56.1.b).1.º: a RETA cuota is due within the
    // same month it corresponds to. Art. 8.b): when the last day of that plazo is inhábil, it ends the previous working day
    // instead, the opposite direction from a tax filing's deadline, which moves forward.
    public static DueWindow MonthlyCuotaSs(YearMonth month, string region, TaxYearConfig config)
    {
        var holidays = Holidays(region, config);
        var start = new DateOnly(month.Year, month.Month, 1);
        var end = new DateOnly(month.Year, month.Month, DateTime.DaysInMonth(month.Year, month.Month));

        while (end.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday || holidays.Contains(end))
        {
            end = end.AddDays(-1);
        }

        return new DueWindow(start, end);
    }

    private static HashSet<DateOnly> Holidays(string region, TaxYearConfig config) =>
        config.Calendar.Holidays
            .Concat(config.Regions.For(region).Holidays)
            .Select(day => day.In(config.TaxYear))
            .ToHashSet();

    private static (DueWindow Window, TraceStep Step) Resolve(
        CalendarWindow? window, string region, TaxYearConfig config, string id, TraceSection section, string title, string calendarName, string entryLabel)
    {
        // SPEC-007 §3: calendar._todo means the year after TaxYear is unpublished. A null window (calendar.renta only) or
        // one that ends in that year cannot be computed without borrowing a date nobody has confirmed yet.
        var note = config.Calendar.DeclaredIncomplete;

        if (window is null)
        {
            throw new ConfigNotFoundException(Invariant(
                $"{entryLabel} of tax year {config.TaxYear} is not in this configuration, which declares the calendar after {config.TaxYear} incomplete: {note}"));
        }

        if (window.End.YearOffset > 0 && note is not null)
        {
            throw new ConfigNotFoundException(Invariant(
                $"{entryLabel} of tax year {config.TaxYear} ends on {Iso(window.End.In(config.TaxYear))}, and this configuration declares the calendar after {config.TaxYear} incomplete: {note}"));
        }

        var holidays = Holidays(region, config);
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
