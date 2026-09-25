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

        if (tramos[0].NetFrom != 0m)
        {
            throw new ArgumentException($"The first tramo must start at 0, got {tramos[0].NetFrom}.", nameof(tramos));
        }

        for (int i = 0; i < tramos.Count; i++)
        {
            var tramo = tramos[i];
            var isLast = i == tramos.Count - 1;

            if (tramo.CuotaMin < 0m)
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

    // BOE tables read "> netFrom y <= netUpTo": the upper bound is inclusive.
    public Tramo For(decimal monthlyNet) =>
        Tramos.First(t => t.NetUpTo is null || monthlyNet <= t.NetUpTo);
}