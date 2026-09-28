using GestorIA.Engine;

namespace GestorIA.Infrastructure.Profiles;

// The taxpayer profile entered once in settings (#69): SPEC-001's TaxpayerProfile as far as the set-aside estimate reads it,
// for one tax year, with the projection of the months of alta in that year. The API validates it before it is built. The
// actuals are not entered here: they come from the profile's movements (Ledger.SetAsideInputAsync).
public sealed record Profile(int TaxYear, TaxpayerProfile Taxpayer, ActivityProjection Projection);
