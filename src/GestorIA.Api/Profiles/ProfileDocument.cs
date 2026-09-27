using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.Json.Serialization;
using GestorIA.Engine;
using GestorIA.Infrastructure.Profiles;
using DomainMoney = GestorIA.Domain.ValueObjects.Money;

namespace GestorIA.Api.Profiles;

// The taxpayer profile on the wire (SPEC-009 §2): the body of POST and PUT /profiles. Amounts are strings in euros with at most
// two decimals; ProfileInput checks each value and names every refused one by its JSON path.
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ProfileInputDocument(int TaxYear, string Region, ProfileEmployment Employment, ProfileRegistration Activity, ProfileProjection Projection);

// What the API answers: the stored profile and the id it is kept under.
public sealed record ProfileView(Guid Id, int TaxYear, string Region, ProfileEmployment Employment, ProfileRegistration Activity, ProfileProjection Projection)
{
    public static ProfileView From(Guid id, Profile profile)
    {
        var (taxpayer, projection) = (profile.Taxpayer, profile.Projection);
        var registration = taxpayer.Activity;

        return new(
            id,
            profile.TaxYear,
            taxpayer.Region,
            new ProfileEmployment(Euros(taxpayer.Employment.Ingresos), Euros(taxpayer.Employment.SeguridadSocial)),
            new ProfileRegistration(
                registration.Alta,
                registration.PreviousYear switch
                {
                    PreviousYear.RendimientoNeto known => new PreviousYearNet(Euros(known.Amount)),
                    _ => new NoActivityLastYear(),
                },
                registration.NewActivity switch
                {
                    NewActivity.Started started => new NewActivityStarted(
                        started.Period == NewActivityPeriod.First ? StartedPeriod.First : StartedPeriod.Following,
                        Euros(started.IngresosFromFormerEmployer)),
                    _ => new EstablishedActivity(),
                }),
            new ProfileProjection(Euros(projection.Ingresos), Euros(projection.Gastos), Euros(projection.BaseCotizacion)));
    }

    // Stored amounts have at most two decimals (ProfileInput), so this writes them exactly.
    private static string Euros(DomainMoney money) => money.Amount.ToString("0.00", CultureInfo.InvariantCulture);
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ProfileEmployment(
    [property: RegularExpression(ProfileAmounts.ZeroOrMore)] string Ingresos,
    [property: RegularExpression(ProfileAmounts.ZeroOrMore)] string SeguridadSocial);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ProfileRegistration(DateOnly Alta, PreviousYearChoice PreviousYear, NewActivityChoice NewActivity);

// JsonPolymorphic writes and reads the subtype by its "kind" member, and the OpenAPI document shows the union as oneOf with
// that discriminator, so the web types get a union the compiler checks rather than a string-or-object guess.
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(NoActivityLastYear), "noActivity")]
[JsonDerivedType(typeof(PreviousYearNet), "rendimientoNeto")]
public abstract record PreviousYearChoice;

public sealed record NoActivityLastYear : PreviousYearChoice;

// The previous year's activity net, negative after a loss (casilla 13 of Modelo 130).
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PreviousYearNet([property: RegularExpression(ProfileAmounts.Signed)] string RendimientoNeto) : PreviousYearChoice;

// LIRPF art. 32.3, as the taxpayer states it.
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(EstablishedActivity), "established")]
[JsonDerivedType(typeof(NewActivityStarted), "started")]
public abstract record NewActivityChoice;

public sealed record EstablishedActivity : NewActivityChoice;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record NewActivityStarted(
    StartedPeriod Period,
    [property: RegularExpression(ProfileAmounts.ZeroOrMore)] string IngresosFromFormerEmployer) : NewActivityChoice;

public enum StartedPeriod
{
    [JsonStringEnumMemberName("first")]
    First,
    [JsonStringEnumMemberName("following")]
    Following,
}

// What the months of alta in the tax year are expected to invoice and spend, the RETA cuota excluded, and the monthly base de
// cotización chosen in Import@ss (src/GestorIA.Cli/README.md, activity.projection).
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ProfileProjection(
    [property: RegularExpression(ProfileAmounts.ZeroOrMore)] string Ingresos,
    [property: RegularExpression(ProfileAmounts.ZeroOrMore)] string Gastos,
    [property: RegularExpression(ProfileAmounts.ZeroOrMore)] string BaseCotizacion);

// Euros and cents as a person types them. Twelve digits before the point fit the numeric(18,6) columns (ADR-0006), and at most
// two after it means nothing typed is rounded on the way into the database. [0-9], not \d, which in .NET matches any
// Unicode digit ("١٢٣") that decimal.Parse then refuses.
public static class ProfileAmounts
{
    public const string ZeroOrMore = @"^[0-9]{1,12}(\.[0-9]{1,2})?$";
    public const string Signed = @"^-?[0-9]{1,12}(\.[0-9]{1,2})?$";
}
