using GestorIA.Domain.ValueObjects;

namespace GestorIA.Engine.Tests;

public class ScaleCalculatorExamples
{
    private static Scale Estatal2025 => TaxYearConfigFiles.Year2025.Irpf.EscalaEstatal;

    private static Scale Valenciana2025 => TaxYearConfigFiles.Year2025.Regions.For("VC").EscalaAutonomica;

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

    // The "Cuota íntegra" column of Ley 13/1997 (CV) art. 2.1 as worded by Ley 9/2022 (BOE-A-1998-8202, version in force
    // from 2023-01-01), the same table as AEAT Manual práctico Renta 2025, cap. 15, Comunitat Valenciana (#48). The last row
    // is the open tranche: 47,700 plus 29.50 % of 50,000.
    public static TheoryData<decimal, decimal> ValencianaCuotaIntegraColumn() => new()
    {
        { 12000m, 1080m },
        { 22000m, 2280m },
        { 32000m, 3780m },
        { 42000m, 5530m },
        { 52000m, 7530m },
        { 62000m, 9780m },
        { 72000m, 12280m },
        { 100000m, 19700m },
        { 150000m, 33450m },
        { 200000m, 47700m },
        { 250000m, 62450m },
    };

    [Theory]
    [MemberData(nameof(ValencianaCuotaIntegraColumn))]
    public void Valenciana2025_ReproducesTheCuotaIntegraColumnOfTheLaw(decimal baseLiquidable, decimal cuota)
    {
        Assert.Equal(cuota, ScaleCalculator.Cuota(Valenciana2025, baseLiquidable));
    }

    [Fact]
    public void BaseInsideOpenTranche_UsesTopRateForTheExcess()
    {
        var expectedUpTo300000 = ScaleCalculator.Cuota(Estatal2025, 300000m);
        Assert.Equal(expectedUpTo300000 + 0.245m * 1000m, ScaleCalculator.Cuota(Estatal2025, 301000m));
    }

    // An attribute cannot hold a decimal literal, and InlineData(12449.99) would pass through a double first (ADR-0004).
    public static TheoryData<decimal, decimal> MarginalRates() => new()
    {
        { 0m, 0.095m },
        { 12449.99m, 0.095m },
        { 12450m, 0.12m },
        { 299999.99m, 0.225m },
        { 300000m, 0.245m },
        { 1000000m, 0.245m },
    };

    [Theory]
    [MemberData(nameof(MarginalRates))]
    public void MarginalRate_IsTheTrancheTheNextEuroFallsIn(decimal baseLiquidable, decimal rate)
    {
        Assert.Equal(new Rate(rate), ScaleCalculator.MarginalRate(Estatal2025, baseLiquidable));
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

    private static Tranche TrancheOf(decimal? upTo, decimal rate) =>
        new(upTo is { } u ? new Money(u) : null, new Rate(rate));
}