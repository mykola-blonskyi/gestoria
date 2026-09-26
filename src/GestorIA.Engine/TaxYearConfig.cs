using GestorIA.Domain.ValueObjects;
using static System.FormattableString;

namespace GestorIA.Engine;

// SPEC-007. Holds the values the set-aside estimator reads (#2); the rest of the file is validated at load but not mapped.
public sealed record TaxYearConfig(
    int TaxYear,
    string ConfigHash,
    IrpfConfig Irpf,
    RegionTable Regions,
    Modelo130Config Modelo130,
    SeguridadSocialConfig SeguridadSocial,
    TaxCalendar Calendar,
    ProvenanceTable Provenance);

public sealed record IrpfConfig(Scale EscalaEstatal, MinimosConfig Minimos, TrabajoConfig Trabajo, ActividadConfig Actividad);

public sealed record MinimosConfig(Money Contribuyente);

public sealed record TrabajoConfig(Money OtrosGastos, ReduccionTrabajoConfig Reduccion);

// K1 and K2 multiply a euro amount and exceed 1, so they are plain coefficients rather than a Rate.
public sealed record ReduccionTrabajoConfig(Money Fixed, Money T1, Money T2, Money T3, decimal K1, decimal K2, Money OtherIncomeCap);

public sealed record ActividadConfig(DificilJustificacionConfig DificilJustificacion, InicioActividadConfig InicioActividad);

public sealed record DificilJustificacionConfig(Rate Pct, Money Max);

// LIRPF art. 32.3: Pct of the positive net, on at most MaxRendimiento of it, unless more than FormerEmployerShare of the
// period's ingresos come from a payer of the taxpayer's employment income in the year before the activity started.
public sealed record InicioActividadConfig(Rate Pct, Money MaxRendimiento, Rate FormerEmployerShare);

public sealed record RegionConfig(string Name, Scale EscalaAutonomica);

public sealed record Modelo130Config(Rate Rate, bool ApplyDj, IReadOnlyList<MinoracionBand> Minoracion);

public sealed record MinoracionBand(Money PrevYearNetUpTo, Money AmountPerQuarter);

public sealed record SeguridadSocialConfig(TramoTable Tramos, TarifaPlana TarifaPlana);

public sealed record TaxCalendar(IReadOnlyList<DueWindow> Modelo130, DueWindow Renta, IReadOnlyList<CalendarDay> Holidays);

public sealed record DueWindow(CalendarDay Start, CalendarDay End);

// A calendar token from the file: "04-20" is 20 April of the tax year, "+1-01-30" is 30 January of the year after.
public readonly record struct CalendarDay(int YearOffset, int Month, int Day)
{
    public override string ToString() =>
        YearOffset == 0 ? Invariant($"{Month:D2}-{Day:D2}") : Invariant($"+{YearOffset}-{Month:D2}-{Day:D2}");
}
