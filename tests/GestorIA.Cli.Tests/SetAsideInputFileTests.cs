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
        var json = Edit(root =>
        {
            var segments = path.Split('.');
            var parent = segments[..^1].Aggregate(root, (node, segment) => node[segment]!);
            parent.AsObject().Remove(segments[^1]);
        });

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
}
