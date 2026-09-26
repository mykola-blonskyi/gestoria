using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using GestorIA.Domain.ValueObjects;
using GestorIA.Engine;

namespace GestorIA.Cli;

// The console's input: the "inputs" object of a set-aside golden (tests/golden/2025/G12.json), amounts as strings.
// Every field is required, since the engine never defaults a fact about the taxpayer. What only the engine can judge,
// such as actuals out of order, it rejects itself.
public static class SetAsideInputFile
{
    public static SetAsideInput Parse(string json, TaxYearConfig config)
    {
        JsonNode? root;

        try
        {
            root = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions { AllowDuplicateProperties = false });
        }
        catch (JsonException e)
        {
            throw new InvalidInputFileException($"The input file is not valid JSON: {e.Message}");
        }

        var profile = Field(root, "profile");
        var employment = Field(profile, "employment");
        var registration = Field(profile, "activity");
        var picture = Field(root, "activity");
        var projection = Field(picture, "projection");

        // "is not JsonArray actuals" names the node as an array for the code after the if, reached only when it is one.
        if (Field(picture, "actuals") is not JsonArray actuals)
        {
            throw Invalid(picture["actuals"]!, "a list of closed quarters, [] when none is closed");
        }

        if (Text(picture, "retenciones") != "foreignPayersOnly")
        {
            throw Invalid(picture["retenciones"]!, "\"foreignPayersOnly\": the estimator covers only clients who withhold no retención (SPEC-003 §0)");
        }

        return new SetAsideInput(
            new TaxpayerProfile(
                Text(profile, "region"),
                new EmploymentIncome(Amount(employment, "ingresos"), Amount(employment, "seguridadSocial")),
                new AutonomoRegistration(Date(registration, "alta"), PreviousYearOf(registration), NewActivityOf(registration))),
            new ActivityPicture(
                [.. actuals.Select(a => new QuarterToDate(QuarterOf(a, "quarter"), Amount(a, "ingresosYtd"), Amount(a, "gastosYtd")))],
                new ActivityProjection(Amount(projection, "ingresos"), Amount(projection, "gastos")),
                new Retenciones.ForeignPayersOnly()),
            config,
            QuarterOf(root, "asOf"));
    }

    private static PreviousYear PreviousYearOf(JsonNode registration) => Field(registration, "previousYear") switch
    {
        JsonValue value when value.GetValueKind() == JsonValueKind.String && value.GetValue<string>() == "noActivity" => new PreviousYear.NoActivity(),
        JsonObject known => new PreviousYear.RendimientoNeto(Amount(known, "rendimientoNeto")),
        var other => throw Invalid(other, "\"noActivity\" or { \"rendimientoNeto\": \"1234.56\" }"),
    };

    private static NewActivity NewActivityOf(JsonNode registration) => Field(registration, "newActivity") switch
    {
        JsonValue value when value.GetValueKind() == JsonValueKind.String && value.GetValue<string>() == "established" => new NewActivity.Established(),
        JsonObject started => new NewActivity.Started(
            Text(started, "period") switch
            {
                "first" => NewActivityPeriod.First,
                "following" => NewActivityPeriod.Following,
                _ => throw Invalid(started["period"]!, "\"first\" or \"following\""),
            },
            Amount(started, "ingresosFromFormerEmployer")),
        var other => throw Invalid(other, "\"established\" or { \"period\": \"first\", \"ingresosFromFormerEmployer\": \"0.00\" }"),
    };

    private static Quarter QuarterOf(JsonNode? parent, string name) => Text(parent, name) switch
    {
        "Q1" => Quarter.Q1,
        "Q2" => Quarter.Q2,
        "Q3" => Quarter.Q3,
        "Q4" => Quarter.Q4,
        _ => throw Invalid(parent![name]!, "one of \"Q1\", \"Q2\", \"Q3\", \"Q4\""),
    };

    private static Money Amount(JsonNode? parent, string name) =>
        decimal.TryParse(Text(parent, name), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount)
            ? new Money(amount)
            : throw Invalid(parent![name]!, "an amount in euros written as a string, like \"1234.56\"");

    private static DateOnly Date(JsonNode? parent, string name) =>
        DateOnly.TryParseExact(Text(parent, name), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : throw Invalid(parent![name]!, "a date written as \"yyyy-MM-dd\"");

    private static string Text(JsonNode? parent, string name) =>
        Field(parent, name) is JsonValue value && value.GetValueKind() == JsonValueKind.String
            ? value.GetValue<string>()
            : throw Invalid(parent![name]!, "a string");

    private static JsonNode Field(JsonNode? parent, string name)
    {
        if (parent is not JsonObject obj)
        {
            throw new InvalidInputFileException($"{parent?.GetPath() ?? "$"} must be an object with a \"{name}\" field.");
        }

        return obj[name] ?? throw new InvalidInputFileException($"{obj.GetPath()}.{name} is missing.");
    }

    // The relaxed encoder echoes "27000 €" as typed instead of escaping it to "27000 €".
    private static readonly JsonSerializerOptions AsTyped = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private static InvalidInputFileException Invalid(JsonNode node, string expected) =>
        new($"{node.GetPath()} is {node.ToJsonString(AsTyped)}; it must be {expected}.");
}
