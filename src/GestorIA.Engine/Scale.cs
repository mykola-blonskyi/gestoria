using GestorIA.Domain.ValueObjects;

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

        var lower = Money.Zero;

        for (int i = 0; i < tranches.Count; i++)
        {
            var tranche = tranches[i];
            var isLast = i == tranches.Count - 1;

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
