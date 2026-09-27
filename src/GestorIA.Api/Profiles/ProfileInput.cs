using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using GestorIA.Engine;
using GestorIA.Infrastructure.Profiles;
using GestorIA.Infrastructure.TaxYears;
using static System.FormattableString;
using DomainMoney = GestorIA.Domain.ValueObjects.Money;

namespace GestorIA.Api.Profiles;

// The boundary between the request body and a Profile. A body of the wrong shape (not JSON, a field missing, unknown or of the
// wrong type) is refused at the first such field, with its path. A body of the right shape has every value checked, and every
// refused one is named at once, so a settings form can mark all its wrong fields in one round trip.
public static class ProfileInput
{
    private static readonly Regex ZeroOrMore = new(ProfileAmounts.ZeroOrMore, RegexOptions.CultureInvariant);
    private static readonly Regex Signed = new(ProfileAmounts.Signed, RegexOptions.CultureInvariant);

    public static async Task<ProfileInputDocument> ReadAsync(HttpRequest request, JsonSerializerOptions options)
    {
        try
        {
            return await JsonSerializer.DeserializeAsync<ProfileInputDocument>(request.Body, options, request.HttpContext.RequestAborted)
                ?? throw new InvalidProfileException(new() { ["$"] = ["The profile is null; it must be an object."] });
        }
        catch (JsonException e)
        {
            var path = e.Path ?? "$";
            throw new InvalidProfileException(new() { [path] = [$"{path}: {e.Message}"] });
        }
        catch (NotSupportedException e)
        {
            // A union without its "kind": the serializer cannot build the abstract type, and names the path only in the message.
            throw new InvalidProfileException(new() { ["$"] = [e.Message] });
        }
    }

    public static Profile Parse(ProfileInputDocument document, TaxYearConfigLoader loader)
    {
        var errors = new Dictionary<string, string[]>();
        var (employment, registration, projection) = (document.Employment, document.Activity, document.Projection);

        // A local function: declared inside Parse, it can add to errors, like a closure in TypeScript.
        DomainMoney Amount(string path, string text, Regex rule, string expected)
        {
            // TryParse as well as the pattern: .NET's $ also matches before a final "\n", which the pattern lets through.
            if (rule.IsMatch(text) && decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount))
            {
                return new DomainMoney(amount);
            }

            errors[path] = [$"{path} is \"{text}\"; it must be {expected}."];
            return DomainMoney.Zero;
        }

        DomainMoney Euros(string path, string text) => Amount(path, text, ZeroOrMore, "an amount in euros of zero or more with at most two decimals, like \"1234.56\"");

        var config = loader.Years().Contains(document.TaxYear) ? loader.Load(document.TaxYear) : null;
        if (config is null)
        {
            errors["$.taxYear"] = [Invariant($"$.taxYear is {document.TaxYear}; the API holds a configuration for {string.Join(", ", loader.Years())} only.")];
        }
        else if (!config.Regions.Usable.ContainsKey(document.Region))
        {
            errors["$.region"] = [Invariant($"$.region is \"{document.Region}\"; {document.TaxYear} covers {string.Join(", ", config.Regions.Usable.Keys.Order(StringComparer.Ordinal))}.")];
        }

        var ingresos = Euros("$.employment.ingresos", employment.Ingresos);
        var seguridadSocial = Euros("$.employment.seguridadSocial", employment.SeguridadSocial);

        // An alta after the tax year leaves no month of activity in it, and the engine refuses every quarter of such a year.
        if (registration.Alta.Year > document.TaxYear)
        {
            errors["$.activity.alta"] = [Invariant($"$.activity.alta is {registration.Alta:yyyy-MM-dd}, after {document.TaxYear}; it must be in {document.TaxYear} or before.")];
        }

        PreviousYear previousYear = registration.PreviousYear switch
        {
            PreviousYearNet known => new PreviousYear.RendimientoNeto(Amount(
                "$.activity.previousYear.rendimientoNeto",
                known.RendimientoNeto,
                Signed,
                "an amount in euros with at most two decimals, negative after a loss, like \"-1234.56\"")),
            _ => new PreviousYear.NoActivity(),
        };

        NewActivity newActivity = registration.NewActivity switch
        {
            NewActivityStarted started => new NewActivity.Started(
                started.Period == StartedPeriod.First ? NewActivityPeriod.First : NewActivityPeriod.Following,
                Euros("$.activity.newActivity.ingresosFromFormerEmployer", started.IngresosFromFormerEmployer)),
            _ => new NewActivity.Established(),
        };

        var baseCotizacion = Euros("$.projection.baseCotizacion", projection.BaseCotizacion);
        if (config is not null && !errors.ContainsKey("$.projection.baseCotizacion"))
        {
            var tramos = config.SeguridadSocial.Tramos;
            if (baseCotizacion < tramos.LowestBase || baseCotizacion > tramos.HighestBase)
            {
                errors["$.projection.baseCotizacion"] =
                [
                    Invariant($"$.projection.baseCotizacion is \"{projection.BaseCotizacion}\"; it must be a base of the {document.TaxYear} tables, from {tramos.LowestBase.Amount} to {tramos.HighestBase.Amount} (LGSS art. 308.1.a 3.ª)."),
                ];
            }
        }

        var profile = new Profile(
            document.TaxYear,
            new TaxpayerProfile(document.Region, new EmploymentIncome(ingresos, seguridadSocial), new AutonomoRegistration(registration.Alta, previousYear, newActivity)),
            new ActivityProjection(Euros("$.projection.ingresos", projection.Ingresos), Euros("$.projection.gastos", projection.Gastos), baseCotizacion));

        return errors.Count == 0 ? profile : throw new InvalidProfileException(errors);
    }
}

public sealed class InvalidProfileException(Dictionary<string, string[]> errors) : Exception("The profile is not valid.")
{
    public IDictionary<string, string[]> Errors { get; } = errors;
}
