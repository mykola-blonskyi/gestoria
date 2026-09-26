namespace GestorIA.Engine;

public static class ScaleCalculator
{
    // SPEC-002 §6, ADR-0004: no rounding here, the casilla mapper rounds.
    public static decimal Cuota(Scale scale, decimal baseLiquidable)
    {
        if (baseLiquidable < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(baseLiquidable), baseLiquidable, "A base liquidable is never negative.");
        }

        decimal total = 0m;
        decimal lower = 0m;

        foreach (var tranche in scale.Tranches)
        {
            var portion = baseLiquidable - lower;

            if (portion <= 0m)
            {
                break;
            }

            if (tranche.UpTo is { } upTo)
            {
                portion = Math.Min(portion, upTo.Amount - lower);
                lower = upTo.Amount;
            }

            total += tranche.Rate.Value * portion;
        }

        return total;
    }
}
