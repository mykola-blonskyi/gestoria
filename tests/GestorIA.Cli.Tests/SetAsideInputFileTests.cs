using System.Globalization;
using System.Text.Json.Nodes;
using GestorIA.Domain.ValueObjects;
using GestorIA.Engine;

namespace GestorIA.Cli.Tests;

public class SetAsideInputFileTests
{
    [Fact]
    public void TheExampleInputParsesIntoTheEngineTypes()
    {
        var input = SetAsideInputFile.Parse(RepoFiles.ExampleInput(), RepoFiles.Config);

        Assert.Equal(Quarter.Q2, input.AsOf);
        Assert.Equal("VC", input.Profile.Region);
        Assert.Equal(new EmploymentIncome(Money.Zero, Money.Zero), input.Profile.Employment);
        Assert.Equal(new DateOnly(2025, 1, 15), input.Profile.Activity.Alta);
        Assert.IsType<PreviousYear.NoActivity>(input.Profile.Activity.PreviousYear);
        var started = Assert.IsType<NewActivity.Started>(input.Profile.Activity.NewActivity);
        Assert.Equal(NewActivityPeriod.First, started.Period);
        Assert.Equal(Money.Zero, started.IngresosFromFormerEmployer);
        Assert.Equal([new QuarterToDate(Quarter.Q1, new Money(6000.00m), new Money(345.33m))], input.Activity.Actuals);
        Assert.Equal(new ActivityProjection(new Money(27000.00m), new Money(900.00m)), input.Activity.Projection);
        Assert.IsType<Retenciones.ForeignPayersOnly>(input.Activity.Retenciones);
        Assert.Same(RepoFiles.Config, input.Config);
    }

    [Fact]
    public void AGoldenWithAnEstablishedActivityAndAKnownPreviousYearParses()
    {
        var input = SetAsideInputFile.Parse(RepoFiles.GoldenInput("G15"), RepoFiles.Config);

        Assert.Equal(new EmploymentIncome(new Money(40000.00m), new Money(2600.00m)), input.Profile.Employment);
        Assert.Equal(new PreviousYear.RendimientoNeto(new Money(30000.00m)), input.Profile.Activity.PreviousYear);
        Assert.IsType<NewActivity.Established>(input.Profile.Activity.NewActivity);
        Assert.Equal([Quarter.Q1, Quarter.Q2], input.Activity.Actuals.Select(a => a.Quarter));
    }

    [Theory]
    [InlineData("profile.employment.seguridadSocial", "$.profile.employment.seguridadSocial is missing.")]
    [InlineData("activity.projection", "$.activity.projection is missing.")]
    [InlineData("asOf", "$.asOf is missing.")]
    [InlineData("profile.activity.newActivity", "$.profile.activity.newActivity is missing.")]
    public void AMissingFieldIsRejectedByItsPath(string path, string message)
    {
        var json = Edit(root => ParentOf(root, path).AsObject().Remove(path.Split('.')[^1]));

        var e = Assert.Throws<InvalidInputFileException>(() => SetAsideInputFile.Parse(json, RepoFiles.Config));
        Assert.Equal(message, e.Message);
    }

    [Theory]
    [InlineData("profile.region", "$.profile.region is null.")]
    [InlineData("activity.actuals.0", "$.activity.actuals[0] is null.")]
    public void ANullIsRejectedAtItsOwnPath(string path, string message)
    {
        var json = Edit(root => Set(root, path, null));

        var e = Assert.Throws<InvalidInputFileException>(() => SetAsideInputFile.Parse(json, RepoFiles.Config));
        Assert.Equal(message, e.Message);
    }

    [Theory]
    [InlineData("profile.employment.ingresos", "$.profile.employment.ingresos")]
    [InlineData("profile.employment.seguridadSocial", "$.profile.employment.seguridadSocial")]
    [InlineData("profile.activity.newActivity.ingresosFromFormerEmployer", "$.profile.activity.newActivity.ingresosFromFormerEmployer")]
    [InlineData("activity.actuals.0.ingresosYtd", "$.activity.actuals[0].ingresosYtd")]
    [InlineData("activity.actuals.0.gastosYtd", "$.activity.actuals[0].gastosYtd")]
    [InlineData("activity.projection.ingresos", "$.activity.projection.ingresos")]
    [InlineData("activity.projection.gastos", "$.activity.projection.gastos")]
    public void ANegativeAmountIsRejected(string path, string jsonPath)
    {
        var json = Edit(root => Set(root, path, "-5"));

        var e = Assert.Throws<InvalidInputFileException>(() => SetAsideInputFile.Parse(json, RepoFiles.Config));
        Assert.Equal($"{jsonPath} is \"-5\"; it must be zero or more.", e.Message);
    }

    [Fact]
    public void APreviousYearLossIsANegativeNet()
    {
        var json = Edit(root => root["profile"]!["activity"]!["previousYear"] = new JsonObject { ["rendimientoNeto"] = "-3000.00" });

        var input = SetAsideInputFile.Parse(json, RepoFiles.Config);

        Assert.Equal(new PreviousYear.RendimientoNeto(new Money(-3000.00m)), input.Profile.Activity.PreviousYear);
    }

    [Theory]
    [InlineData("taxYear", "$.taxYear is not a field the estimator reads; the fields of $ are asOf, profile, activity.")]
    [InlineData("profile.activity.tarifaPlana", "$.profile.activity.tarifaPlana is not a field the estimator reads; the fields of $.profile.activity are alta, previousYear, newActivity.")]
    [InlineData("profile.activity.newActivity.startedOn", "$.profile.activity.newActivity.startedOn is not a field the estimator reads; the fields of $.profile.activity.newActivity are period, ingresosFromFormerEmployer.")]
    [InlineData("activity.actuals.0.note", "$.activity.actuals[0].note is not a field the estimator reads; the fields of $.activity.actuals[0] are quarter, ingresosYtd, gastosYtd.")]
    [InlineData("activity.projection.iva", "$.activity.projection.iva is not a field the estimator reads; the fields of $.activity.projection are ingresos, gastos.")]
    public void AnUnknownFieldIsRejected(string path, string message)
    {
        var json = Edit(root => Set(root, path, false));

        var e = Assert.Throws<InvalidInputFileException>(() => SetAsideInputFile.Parse(json, RepoFiles.Config));
        Assert.Equal(message, e.Message);
    }

    [Theory]
    [InlineData("\"27,000.00\"", "$.activity.projection.ingresos is \"27,000.00\"; it must be an amount in euros written as a string, like \"1234.56\".")]
    [InlineData("\"27000 €\"", "$.activity.projection.ingresos is \"27000 €\"; it must be an amount in euros written as a string, like \"1234.56\".")]
    [InlineData("27000", "$.activity.projection.ingresos is 27000; it must be a string.")]
    public void AnAmountThatIsNotADecimalStringIsRejected(string value, string message)
    {
        var json = Edit(root => root["activity"]!["projection"]!["ingresos"] = JsonNode.Parse(value));

        var e = Assert.Throws<InvalidInputFileException>(() => SetAsideInputFile.Parse(json, RepoFiles.Config));
        Assert.Equal(message, e.Message);
    }

    [Theory]
    [InlineData("asOf", "\"Q5\"", "$.asOf is \"Q5\"; it must be one of \"Q1\", \"Q2\", \"Q3\", \"Q4\".")]
    [InlineData("asOf", "\"2\"", "$.asOf is \"2\"; it must be one of \"Q1\", \"Q2\", \"Q3\", \"Q4\".")]
    [InlineData("alta", "\"2025-02-30\"", "$.profile.activity.alta is \"2025-02-30\"; it must be a date written as \"yyyy-MM-dd\".")]
    [InlineData("alta", "\"15/01/2025\"", "$.profile.activity.alta is \"15/01/2025\"; it must be a date written as \"yyyy-MM-dd\".")]
    [InlineData("previousYear", "\"none\"", "$.profile.activity.previousYear is \"none\"; it must be \"noActivity\" or { \"rendimientoNeto\": \"1234.56\" }.")]
    [InlineData("newActivity", "\"new\"", "$.profile.activity.newActivity is \"new\"; it must be \"established\" or { \"period\": \"first\", \"ingresosFromFormerEmployer\": \"0.00\" }.")]
    [InlineData("retenciones", "\"withheld\"", "$.activity.retenciones is \"withheld\"; it must be \"foreignPayersOnly\": the estimator covers only clients who withhold no retención (SPEC-003 §0).")]
    [InlineData("actuals", "{}", "$.activity.actuals is {}; it must be a list of closed quarters, [] when none is closed.")]
    public void ABadValueIsRejectedWithWhatItMustBe(string field, string value, string message)
    {
        var json = Edit(root =>
        {
            var parent = field switch
            {
                "asOf" => root,
                "alta" or "previousYear" or "newActivity" => root["profile"]!["activity"]!,
                _ => root["activity"]!,
            };
            parent[field] = JsonNode.Parse(value);
        });

        var e = Assert.Throws<InvalidInputFileException>(() => SetAsideInputFile.Parse(json, RepoFiles.Config));
        Assert.Equal(message, e.Message);
    }

    [Fact]
    public void ADuplicatedFieldIsRejected()
    {
        var json = RepoFiles.ExampleInput().Replace("\"asOf\": \"Q2\",", "\"asOf\": \"Q2\", \"asOf\": \"Q3\",", StringComparison.Ordinal);

        var e = Assert.Throws<InvalidInputFileException>(() => SetAsideInputFile.Parse(json, RepoFiles.Config));
        Assert.StartsWith("The input file is not valid JSON: Duplicate property 'asOf'", e.Message, StringComparison.Ordinal);
    }

    private static string Edit(Action<JsonNode> change)
    {
        var root = JsonNode.Parse(RepoFiles.ExampleInput())!;
        change(root);
        return root.ToJsonString();
    }

    // A dotted path such as "activity.actuals.0.gastosYtd", where a number indexes an array.
    private static void Set(JsonNode root, string path, JsonNode? value)
    {
        var parent = ParentOf(root, path);
        var last = path.Split('.')[^1];

        if (parent is JsonArray array)
        {
            array[int.Parse(last, CultureInfo.InvariantCulture)] = value;
        }
        else
        {
            parent[last] = value;
        }
    }

    private static JsonNode ParentOf(JsonNode root, string path) =>
        path.Split('.')[..^1].Aggregate(root, (node, segment) =>
            node is JsonArray array ? array[int.Parse(segment, CultureInfo.InvariantCulture)]! : node[segment]!);
}
