using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using GestorIA.Engine;

namespace GestorIA.Api.SetAside;

// The console's input file (src/GestorIA.Cli/README.md), described for the OpenAPI document and nothing else: the endpoint
// hands the raw body to SetAsideInputFile, the console's own parser, so both refuse the same inputs with the same messages.
// Api.Tests validates every set-aside golden's inputs against the generated document, so the two cannot drift apart.
[Description("The console's input file, byte for byte: src/GestorIA.Cli/README.md describes every field.")]
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SetAsideInputDocument(Quarter AsOf, ProfileDocument Profile, ActivityDocument Activity);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ProfileDocument(string Region, EmploymentDocument Employment, RegistrationDocument Activity);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record EmploymentDocument(
    [property: RegularExpression(Amounts.ZeroOrMore)] string Ingresos,
    [property: RegularExpression(Amounts.ZeroOrMore)] string SeguridadSocial);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RegistrationDocument(DateOnly Alta, PreviousYearDocument PreviousYear, NewActivityDocument NewActivity);

// "noActivity", or { "rendimientoNeto": "8000.00" }. UnionSchemas writes the schema; C# has no string-or-object type.
public sealed record PreviousYearDocument;

// "established", or { "period": "first" | "following", "ingresosFromFormerEmployer": "0.00" }. UnionSchemas writes the schema.
public sealed record NewActivityDocument;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ActivityDocument(
    RetencionesDocument Retenciones,
    IReadOnlyList<QuarterToDateDocument> Actuals,
    ProjectionDocument Projection);

public enum RetencionesDocument
{
    // JsonStringEnumMemberName is the string the member is written as in JSON, here and in the OpenAPI enum.
    [JsonStringEnumMemberName("foreignPayersOnly")]
    ForeignPayersOnly,
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record QuarterToDateDocument(
    Quarter Quarter,
    [property: RegularExpression(Amounts.ZeroOrMore)] string IngresosYtd,
    [property: RegularExpression(Amounts.ZeroOrMore)] string GastosYtd,
    [property: RegularExpression(Amounts.ZeroOrMore)] string CuotasSsYtd);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ProjectionDocument(
    [property: RegularExpression(Amounts.ZeroOrMore)] string Ingresos,
    [property: RegularExpression(Amounts.ZeroOrMore)] string Gastos,
    [property: RegularExpression(Amounts.ZeroOrMore)] string BaseCotizacion);

// Amounts are strings with a decimal point and no thousands separator (SPEC-009 §1); only the previous year's net may be negative.
public static class Amounts
{
    public const string ZeroOrMore = @"^\d+(\.\d+)?$";
    public const string Signed = @"^-?\d+(\.\d+)?$";

    // What the API answers: euros rounded to the cent, always two decimals.
    public const string Cents = @"^-?\d+\.\d{2}$";
}
