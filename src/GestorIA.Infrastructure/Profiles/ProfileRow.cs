using GestorIA.Domain.ValueObjects;
using GestorIA.Engine;

namespace GestorIA.Infrastructure.Profiles;

// The profile as a table row (ADR-0006: money in numeric(18,6)). The two either-or facts of the registration become nullable
// columns: PreviousYearRendimientoNeto is null when there was no activity last year, NewActivityPeriod and
// IngresosFromFormerEmployer are null together when the activity is not new. GestoriaDbContext holds the check constraints.
public sealed class ProfileRow
{
    public Guid Id { get; set; }

    public int TaxYear { get; set; }

    // "required" makes an object initializer set the property, so a row is never built without a region.
    public required string Region { get; set; }

    public decimal EmploymentIngresos { get; set; }

    public decimal EmploymentSeguridadSocial { get; set; }

    public DateOnly Alta { get; set; }

    public decimal? PreviousYearRendimientoNeto { get; set; }

    public NewActivityPeriod? NewActivityPeriod { get; set; }

    public decimal? IngresosFromFormerEmployer { get; set; }

    public decimal ProjectionIngresos { get; set; }

    public decimal ProjectionGastos { get; set; }

    public decimal BaseCotizacion { get; set; }

    public static ProfileRow From(Profile profile)
    {
        var row = new ProfileRow { Region = profile.Taxpayer.Region };
        row.Write(profile);
        return row;
    }

    public void Write(Profile profile)
    {
        var (taxpayer, projection) = (profile.Taxpayer, profile.Projection);
        TaxYear = profile.TaxYear;
        Region = taxpayer.Region;
        EmploymentIngresos = taxpayer.Employment.Ingresos.Amount;
        EmploymentSeguridadSocial = taxpayer.Employment.SeguridadSocial.Amount;
        Alta = taxpayer.Activity.Alta;
        PreviousYearRendimientoNeto = taxpayer.Activity.PreviousYear is PreviousYear.RendimientoNeto known ? known.Amount.Amount : null;
        (NewActivityPeriod, IngresosFromFormerEmployer) = taxpayer.Activity.NewActivity is NewActivity.Started started
            ? (started.Period, started.IngresosFromFormerEmployer.Amount)
            : ((NewActivityPeriod?)null, (decimal?)null);
        ProjectionIngresos = projection.Ingresos.Amount;
        ProjectionGastos = projection.Gastos.Amount;
        BaseCotizacion = projection.BaseCotizacion.Amount;
    }

    public Profile ToProfile() => new(
        TaxYear,
        new TaxpayerProfile(
            Region,
            new EmploymentIncome(Cents(EmploymentIngresos), Cents(EmploymentSeguridadSocial)),
            new AutonomoRegistration(
                Alta,
                PreviousYearRendimientoNeto is { } net ? new PreviousYear.RendimientoNeto(Cents(net)) : new PreviousYear.NoActivity(),
                NewActivityPeriod is { } period ? new NewActivity.Started(period, Cents(IngresosFromFormerEmployer!.Value)) : new NewActivity.Established())),
        new ActivityProjection(Cents(ProjectionIngresos), Cents(ProjectionGastos), Cents(BaseCotizacion)));

    // A decimal keeps its scale, and numeric(18,6) answers 30000.00 as 30000.000000, which the trace would print as such. What
    // is stored has at most two decimals (the API refuses more), so rounding to two only drops zeros and never changes a value:
    // every estimate of a stored profile shows its amounts in cents.
    private static Money Cents(decimal stored) => new(decimal.Round(stored, 2));
}
