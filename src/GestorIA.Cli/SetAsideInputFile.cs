using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using GestorIA.Domain.ValueObjects;
using GestorIA.Engine;
using static System.FormattableString;

namespace GestorIA.Cli;

// The console's input: the "inputs" object of a set-aside golden (tests/golden/2025/G12.json), amounts as strings.
// Every field is required and no other is allowed, since the engine never defaults a fact about the taxpayer and a field
// it does not read would be ignored silently. What only the engine can judge, such as actuals out of order, it rejects itself.
public static class SetAsideInputFile
{
    public static SetAsideInput Parse(string json, TaxYearConfig config)
    {
        JsonNode? parsed;

        try
        {
            parsed = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions { AllowDuplicateProperties = false });
        }
        catch (JsonException e)
        {
            throw new InvalidInputFileException($"The input file is not valid JSON: {e.Message}");
        }

        var root = Object(parsed ?? throw new InvalidInputFileException("$ is null; it must be an object."), "asOf", "profile", "activity");
        var profile = Object(Field(root, "profile"), "region", "employment", "activity");
        var employment = Object(Field(profile, "employment"), "ingresos", "seguridadSocial");
        var registration = Object(Field(profile, "activity"), "alta", "previousYear", "newActivity");
        var picture = Object(Field(root, "activity"), "retenciones", "actuals", "projection");
        var projection = Object(Field(picture, "projection"), "ingresos", "gastos");

        // "is not JsonArray actuals" names the node as an array for the code after the if, reached only when it is one.
        if (Field(picture, "actuals") is not JsonArray actuals)
        {
            throw Invalid(picture["actuals"]!, "a list of closed quarters, [] when none is closed");
        }

        if (Text(picture, "retenciones") != "foreignPayersOnly")
        {
            throw Invalid(picture["retenciones"]!, "\"foreignPayersOnly\": the estimator covers only clients who withhold no retención (SPEC-003 §0)");
        }

        var closed = actuals
            .Select((actual, index) => Object(
                actual ?? throw new InvalidInputFileException(Invariant($"{actuals.GetPath()}[{index}] is null.")),
                "quarter",
                "ingresosYtd",
                "gastosYtd"))
            .Select(actual => new QuarterToDate(QuarterOf(actual, "quarter"), Amount(actual, "ingresosYtd"), Amount(actual, "gastosYtd")))
            .ToList();

        return new SetAsideInput(
            new TaxpayerProfile(
                Text(profile, "region"),
                new EmploymentIncome(Amount(employment, "ingresos"), Amount(employment, "seguridadSocial")),
                new AutonomoRegistration(Date(registration, "alta"), PreviousYearOf(registration), NewActivityOf(registration))),
            new ActivityPicture(
                closed,
                new ActivityProjection(Amount(projection, "ingresos"), Amount(projection, "gastos")),
                new Retenciones.ForeignPayersOnly()),
            config,
            QuarterOf(root, "asOf"));
    }

    // The previous year's net is the one amount that may be negative: a loss that year.
    private static PreviousYear PreviousYearOf(JsonObject registration) => Field(registration, "previousYear") switch
    {
        JsonValue value when value.GetValueKind() == JsonValueKind.String && value.GetValue<string>() == "noActivity" => new PreviousYear.NoActivity(),
        JsonObject known => new PreviousYear.RendimientoNeto(SignedAmount(Object(known, "rendimientoNeto"), "rendimientoNeto")),
        var other => throw Invalid(other, "\"noActivity\" or { \"rendimientoNeto\": \"1234.56\" }"),
    };

    private static NewActivity NewActivityOf(JsonObject registration) => Field(registration, "newActivity") switch
    {
        JsonValue value when value.GetValueKind() == JsonValueKind.String && value.GetValue<string>() == "established" => new NewActivity.Established(),
        JsonObject started => StartedOf(Object(started, "period", "ingresosFromFormerEmployer")),
        var other => throw Invalid(other, "\"established\" or { \"period\": \"first\", \"ingresosFromFormerEmployer\": \"0.00\" }"),
    };

    private static NewActivity.Started StartedOf(JsonObject started) => new(
        Text(started, "period") switch
        {
            "first" => NewActivityPeriod.First,
            "following" => NewActivityPeriod.Following,
            _ => throw Invalid(started["period"]!, "\"first\" or \"following\""),
        },
        Amount(started, "ingresosFromFormerEmployer"));

    private static Quarter QuarterOf(JsonObject parent, string name) => Text(parent, name) switch
    {
        "Q1" => Quarter.Q1,
        "Q2" => Quarter.Q2,
        "Q3" => Quarter.Q3,
        "Q4" => Quarter.Q4,
        _ => throw Invalid(parent[name]!, "one of \"Q1\", \"Q2\", \"Q3\", \"Q4\""),
    };

    private static Money Amount(JsonObject parent, string name)
    {
        var amount = SignedAmount(parent, name);
        return amount >= Money.Zero ? amount : throw Invalid(parent[name]!, "zero or more");
    }

    private static Money SignedAmount(JsonObject parent, string name) =>
        decimal.TryParse(Text(parent, name), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount)
            ? new Money(amount)
            : throw Invalid(parent[name]!, "an amount in euros written as a string, like \"1234.56\"");

    private static DateOnly Date(JsonObject parent, string name) =>
        DateOnly.TryParseExact(Text(parent, name), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : throw Invalid(parent[name]!, "a date written as \"yyyy-MM-dd\"");

    private static string Text(JsonObject parent, string name) =>
        Field(parent, name) is JsonValue value && value.GetValueKind() == JsonValueKind.String
            ? value.GetValue<string>()
            : throw Invalid(parent[name]!, "a string");

    private static JsonNode Field(JsonObject parent, string name)
    {
        // TryGetPropertyValue tells a missing field apart from one present as JSON null, which the indexer returns alike.
        if (!parent.TryGetPropertyValue(name, out var value))
        {
            throw new InvalidInputFileException($"{parent.GetPath()}.{name} is missing.");
        }

        return value ?? throw new InvalidInputFileException($"{parent.GetPath()}.{name} is null.");
    }

    private static JsonObject Object(JsonNode node, params string[] fields)
    {
        if (node is not JsonObject obj)
        {
            throw Invalid(node, "an object");
        }

        var unknown = obj.Select(property => property.Key).FirstOrDefault(key => !fields.Contains(key));
        if (unknown is not null)
        {
            throw new InvalidInputFileException(
                $"{obj.GetPath()}.{unknown} is not a field the estimator reads; the fields of {obj.GetPath()} are {string.Join(", ", fields)}.");
        }

        return obj;
    }

    // The relaxed encoder echoes "27000 €" as typed instead of escaping it to "27000 \u20AC".
    private static readonly JsonSerializerOptions AsTyped = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private static InvalidInputFileException Invalid(JsonNode node, string expected) =>
        new($"{node.GetPath()} is {node.ToJsonString(AsTyped)}; it must be {expected}.");
}
