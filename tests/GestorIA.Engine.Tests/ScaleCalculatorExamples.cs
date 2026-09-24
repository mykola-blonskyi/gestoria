namespace GestorIA.Engine.Tests;

public class ScaleCalculatorExamples
{
    // config/tax-years/2025.example.json /irpf/escalaEstatal
    internal static readonly Scale Estatal2025 = new(
    [
        new Tranche(12450m, 0.095m),
        new Tranche(20200m, 0.12m),
        new Tranche(35200m, 0.15m),
        new Tranche(60000m, 0.185m),
        new Tranche(300000m, 0.225m),
        new Tranche(null, 0.245m),
    ]);

    // config/tax-years/2025.example.json /regions/VC/escalaAutonomica
    internal static readonly Scale Valenciana2025 = new(
    [
        new Tranche(12000m, 0.09m),
        new Tranche(22000m, 0.12m),
        new Tranche(32000m, 0.15m),
        new Tranche(42000m, 0.175m),
        new Tranche(52000m, 0.199m),
        new Tranche(62000m, 0.224m),
        new Tranche(72000m, 0.249m),
        new Tranche(100000m, 0.266m),
        new Tranche(150000m, 0.279m),
        new Tranche(200000m, 0.289m),
        new Tranche(null, 0.295m),
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
        Assert.Throws<ArgumentException>(() => new Scale([new Tranche(null, 0.1m), new Tranche(100m, 0.2m)]));
    }

    [Fact]
    public void Scale_RejectsNonAscendingUpTo()
    {
        Assert.Throws<ArgumentException>(() => new Scale([new Tranche(100m, 0.1m), new Tranche(100m, 0.2m), new Tranche(null, 0.3m)]));
    }

    [Fact]
    public void Scale_RejectsClosedLastTranche()
    {
        Assert.Throws<ArgumentException>(() => new Scale([new Tranche(100m, 0.1m)]));
    }
}
