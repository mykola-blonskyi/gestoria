using GestorIA.Engine;

namespace GestorIA.Infrastructure.Profiles;

// The taxpayer profile entered once in settings (#69): SPEC-001's TaxpayerProfile as far as the set-aside estimate reads it,
// for one tax year, with the projection of the months of alta in that year. The API validates it before it is built.
public sealed record Profile(int TaxYear, TaxpayerProfile Taxpayer, ActivityProjection Projection)
{
    // No closed quarter is stated, so the projection covers every month of alta in the year. Actuals are what the ledger will
    // hold once transactions exist (SPEC-001 §4); the profile is not where they are entered.
    public SetAsideInput SetAsideInput(TaxYearConfig config, Quarter asOf) =>
        new(Taxpayer, new ActivityPicture([], Projection, new Retenciones.ForeignPayersOnly()), config, asOf);
}
