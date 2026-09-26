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

    public static DueWindow Modelo130(Quarter quarter, string region, TaxYearConfig config) =>
        Resolve(config.Calendar.Modelo130[(int)quarter - 1], region, config);

    public static DueWindow Renta(string region, TaxYearConfig config) => Resolve(config.Calendar.Renta, region, config);

    private static DueWindow Resolve(CalendarWindow window, string region, TaxYearConfig config)
    {
        var holidays = config.Calendar.Holidays
            .Concat(config.Regions.For(region).Holidays)
            .Select(day => day.In(config.TaxYear))
            .ToHashSet();
        var end = window.End.In(config.TaxYear);

        while (end.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday || holidays.Contains(end))
        {
            end = end.AddDays(1);
        }

        return new DueWindow(window.Start.In(config.TaxYear), end);
    }
}
