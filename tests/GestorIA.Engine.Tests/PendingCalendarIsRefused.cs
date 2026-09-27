using System.Text;
using System.Text.Json.Nodes;
using GestorIA.Domain.ValueObjects;
using GestorIA.Infrastructure.TaxYears;

namespace GestorIA.Engine.Tests;

// SPEC-007 §2, §3 (#47): a calendar._todo declares the year after the tax year unpublished. Built from 2025.example.json
// with that year's calendar removed, this is the shape a real file carries before the AGE resolución and the Orden HAC land.
public class PendingCalendarIsRefused
{
    private const string Note = "2026 días inhábiles (AGE resolución) and the Renta 2026 window (Orden HAC) are not published yet";

    private static TaxYearConfig Build()
    {
        var root = JsonNode.Parse(File.ReadAllBytes(Path.Combine(TaxYearConfigFiles.Root(), TaxYearConfigFiles.Example2025)))!;

        root["calendar"]!["_todo"] = Note;
        root["calendar"]!["renta"] = null;
        root["provenance"]!.AsObject().Remove("/calendar/renta");
        RemoveNextYear(root["calendar"]!["holidays"]!.AsArray());
        foreach (var (_, region) in root["regions"]!.AsObject())
        {
            RemoveNextYear(region!["holidays"]!.AsArray());
        }

        return TaxYearConfigParser.Parse(Encoding.UTF8.GetBytes(root.ToJsonString()), TaxYearConfigFiles.Example2025);
    }

    private static void RemoveNextYear(JsonArray days)
    {
        foreach (var day in days.Where(d => d!.GetValue<string>().StartsWith("+1-", StringComparison.Ordinal)).ToList())
        {
            days.Remove(day);
        }
    }

    [Fact]
    public void APendingCalendarStillLoads()
    {
        var config = Build();

        Assert.Null(config.Calendar.Renta);
        Assert.Equal(Note, config.Calendar.DeclaredIncomplete);
    }

    [Fact]
    public void RentaOfAPendingCalendarIsRefused()
    {
        var config = Build();

        var error = Assert.Throws<ConfigNotFoundException>(() => FilingDeadline.Renta("VC", config));

        Assert.Contains("calendar.renta", error.Message, StringComparison.Ordinal);
        Assert.Contains(Note, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Modelo130Q4OfAPendingCalendarIsRefused()
    {
        var config = Build();

        var error = Assert.Throws<ConfigNotFoundException>(() => FilingDeadline.Modelo130(Quarter.Q4, "VC", config));

        Assert.Contains("calendar.modelo130", error.Message, StringComparison.Ordinal);
        Assert.Contains(Note, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Modelo130Q1OfAPendingCalendarStillComputesTheSameDueDate()
    {
        var pending = Build();
        var unmutated = TaxYearConfigFiles.Year2025;

        var (pendingWindow, _) = FilingDeadline.Modelo130(Quarter.Q1, "VC", pending);
        var (unmutatedWindow, _) = FilingDeadline.Modelo130(Quarter.Q1, "VC", unmutated);

        Assert.Equal(unmutatedWindow.End, pendingWindow.End);
    }

    [Fact]
    public void TheAnnualTrueUpOnAPendingCalendarIsRefused()
    {
        var config = Build();

        Assert.Throws<ConfigNotFoundException>(() => AnnualTrueUpCalculator.Gap(
            new AnnualTrueUpInput(
                new EmploymentIncome(new Money(30000m), new Money(1950m)),
                new ActivityIncome(Money.Zero, Money.Zero, new NewActivity.Established()),
                Money.Zero,
                "VC"),
            config));
    }
}
