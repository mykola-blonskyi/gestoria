using GestorIA.Domain.ValueObjects;

namespace GestorIA.Engine.Tests;

public class ScaleCalculatorExamples
{
    // config/tax-years/2025.example.json /irpf/escalaEstatal
    internal static readonly Scale Estatal2025 = new(
    [
        TrancheOf(12450m, 0.095m),
        TrancheOf(20200m, 0.12m),
        TrancheOf(35200m, 0.15m),
        TrancheOf(60000m, 0.185m),
        TrancheOf(300000m, 0.225m),
        TrancheOf(null, 0.245m),
    ]);

    // config/tax-years/2025.example.json /regions/VC/escalaAutonomica
    internal static readonly Scale Valenciana2025 = new(
    [
        TrancheOf(12000m, 0.09m),
        TrancheOf(22000m, 0.12m),
        TrancheOf(32000m, 0.15m),
        TrancheOf(42000m, 0.175m),
        TrancheOf(52000m, 0.199m),
        TrancheOf(62000m, 0.224m),
        TrancheOf(72000m, 0.249m),
        TrancheOf(100000m, 0.266m),
        TrancheOf(150000m, 0.279m),
        TrancheOf(200000m, 0.289m),
        TrancheOf(null, 0.295m),
    ]);

    [Fact]
    public void GoldenG1_EstatalOnBase26050_Is2990_25()
    {
        Assert.Equal(2990.25m, ScaleCalculator.Cuota(Estatal2025, 26050m));
    }

    [Fact]
    public void GoldenG1_ValencianaOnBase26050_Is2887_50()
    {
        Assert.Equal(2887.50m, ScaleCalculator.Cuota(Valenciana2025, 26050m));
    }

    [Fact]
    public void BaseInsideOpenTranche_UsesTopRateForTheExcess()
    {
        var expectedUpTo300000 = ScaleCalculator.Cuota(Estatal2025, 300000m);
        Assert.Equal(expectedUpTo300000 + 0.245m * 1000m, ScaleCalculator.Cuota(Estatal2025, 301000m));
    }

    [Fact]
    public void NegativeBase_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ScaleCalculator.Cuota(Estatal2025, -1m));
    }

    [Fact]
    public void Scale_RejectsOpenTrancheThatIsNotLast()
    {
        Assert.Throws<ArgumentException>(() => new Scale([TrancheOf(null, 0.1m), TrancheOf(100m, 0.2m)]));
    }

    [Fact]
    public void Scale_RejectsNonAscendingUpTo()
    {
        Assert.Throws<ArgumentException>(() => new Scale([TrancheOf(100m, 0.1m), TrancheOf(100m, 0.2m), TrancheOf(null, 0.3m)]));
    }

    [Fact]
    public void Scale_RejectsClosedLastTranche()
    {
        Assert.Throws<ArgumentException>(() => new Scale([TrancheOf(100m, 0.1m)]));
    }

    internal static Tranche TrancheOf(decimal? upTo, decimal rate) =>
        new(upTo is { } u ? new Money(u) : null, new Rate(rate));
}