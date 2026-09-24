namespace GestorIA.Engine;

public sealed class Scale
{
    public IReadOnlyList<Tranche> Tranches { get; }

    public Scale(IReadOnlyList<Tranche> tranches)
    {
        if (tranches.Count == 0)
        {
            throw new ArgumentException("A scale needs at least one tranche.", nameof(tranches));
        }

        decimal lower = 0m;

        for (int i = 0; i < tranches.Count; i++)
        {
            var tranche = tranches[i];
            var isLast = i == tranches.Count - 1;

            if (tranche.Rate < 0m || tranche.Rate > 1m)
            {
                throw new ArgumentException($"Tranche {i} has rate {tranche.Rate}, outside [0, 1].", nameof(tranches));
            }

            if (isLast)
            {
                if (tranche.UpTo is not null)
                {
                    throw new ArgumentException($"The last tranche must be open (upTo: null), got {tranche.UpTo}.", nameof(tranches));
                }

                continue;
            }

            if (tranche.UpTo is not { } upTo)
            {
                throw new ArgumentException($"Tranche {i} is open but is not the last one.", nameof(tranches));
            }

            if (upTo <= lower)
            {
                throw new ArgumentException($"Tranche {i} ends at {upTo}, which is not above {lower}.", nameof(tranches));
            }

            lower = upTo;
        }

        Tranches = tranches;
    }
}
