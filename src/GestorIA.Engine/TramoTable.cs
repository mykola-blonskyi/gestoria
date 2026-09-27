using GestorIA.Domain.ValueObjects;

namespace GestorIA.Engine;

public sealed class TramoTable
{
    public IReadOnlyList<Tramo> Tramos { get; }

    public TramoTable(IReadOnlyList<Tramo> tramos)
    {
        if (tramos.Count == 0)
        {
            throw new ArgumentException("A tramo table needs at least one tramo.", nameof(tramos));
        }

        if (tramos[0].NetFrom != Money.Zero)
        {
            throw new ArgumentException($"The first tramo must start at 0, got {tramos[0].NetFrom}.", nameof(tramos));
        }

        for (int i = 0; i < tramos.Count; i++)
        {
            var tramo = tramos[i];
            var isLast = i == tramos.Count - 1;

            if (tramo.CuotaMin < Money.Zero)
            {
                throw new ArgumentException($"Tramo {i} has cuotaMin {tramo.CuotaMin}, below zero.", nameof(tramos));
            }

            if (isLast)
            {
                if (tramo.NetUpTo is not null)
                {
                    throw new ArgumentException($"The last tramo must be open (netUpTo: null), got {tramo.NetUpTo}.", nameof(tramos));
                }

                continue;
            }

            if (tramo.NetUpTo is not { } upTo)
            {
                throw new ArgumentException($"Tramo {i} is open but is not the last one.", nameof(tramos));
            }

            if (upTo <= tramo.NetFrom)
            {
                throw new ArgumentException($"Tramo {i} ends at {upTo}, which is not above {tramo.NetFrom}.", nameof(tramos));
            }

            if (tramos[i + 1].NetFrom != upTo)
            {
                throw new ArgumentException($"Tramo {i + 1} starts at {tramos[i + 1].NetFrom}, leaving a gap after {upTo}.", nameof(tramos));
            }
        }

        Tramos = tramos;
    }

    // LGSS art. 308.1.a 3.ª: the taxpayer may choose any base of the year's tables, from the lowest base mínima (the reduced
    // table's first tramo) to the highest base máxima (the general table's last); TGSS accepts no other.
    public Money LowestBase => Tramos.Min(t => t.BaseMin);

    public Money HighestBase => Tramos.Max(t => t.BaseMax);

    // BOE tables read "> netFrom y <= netUpTo": the upper bound is inclusive.
    public Tramo For(decimal monthlyNet) =>
        Tramos.First(t => t.NetUpTo is not { } upTo || monthlyNet <= upTo.Amount);
}