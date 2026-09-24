using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;

namespace GestorIA.Engine.Tests;

public class ScaleCalculatorProperties
{
    private const int MaxCents = 100_000_000; // 1,000,000.00 €

    private static readonly Gen<decimal> Base =
        Gen.Choose(0, MaxCents).Select(cents => cents / 100m);

    private static readonly Gen<Scale> ValidScale =
        from count in Gen.Choose(1, 8)
        from boundaries in Gen.Choose(1, 50_000_00).ListOf(count - 1)
        from rates in Gen.Choose(0, 1000).ListOf(count)
        select Build(boundaries, rates);

    private static Scale Build(IList<int> boundaries, IList<int> rates)
    {
        var upTos = boundaries.Select(b => (decimal)b / 100m).Distinct().Order().ToList();
        var tranches = new List<Tranche>();

        for (var i = 0; i < upTos.Count; i++)
        {
            tranches.Add(new Tranche(upTos[i], rates[i] / 1000m));
        }

        tranches.Add(new Tranche(null, rates[upTos.Count] / 1000m));

        return new Scale(tranches);
    }

    [Property]
    public Property CuotaAtZeroIsZero()
    {
        return Prop.ForAll(ValidScale.ToArbitrary(), scale =>
            ScaleCalculator.Cuota(scale, 0m) == 0m);
    }

    [Property]
    public Property CuotaIsMonotonicNonDecreasing()
    {
        return Prop.ForAll(ValidScale.ToArbitrary(), Base.ToArbitrary(), Base.ToArbitrary(), (scale, a, b) =>
        {
            var (low, high) = a <= b ? (a, b) : (b, a);
            return ScaleCalculator.Cuota(scale, low) <= ScaleCalculator.Cuota(scale, high);
        });
    }

    [Property]
    public Property CuotaIsContinuousAtEveryBoundary()
    {
        const decimal epsilon = 0.01m;

        return Prop.ForAll(ValidScale.ToArbitrary(), scale =>
        {
            for (var i = 0; i < scale.Tranches.Count - 1; i++)
            {
                var boundary = scale.Tranches[i].UpTo!.Value;
                var rateBelow = scale.Tranches[i].Rate;
                var rateAbove = scale.Tranches[i + 1].Rate;

                var at = ScaleCalculator.Cuota(scale, boundary);
                var justBelow = ScaleCalculator.Cuota(scale, boundary - epsilon);
                var justAbove = ScaleCalculator.Cuota(scale, boundary + epsilon);

                if (at - justBelow != rateBelow * epsilon) { return false; }
                if (justAbove - at != rateAbove * epsilon) { return false; }
            }

            return true;
        });
    }
}
