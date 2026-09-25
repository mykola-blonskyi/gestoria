namespace GestorIA.Domain.ValueObjects;

// SPEC-001 §2: a fraction in [0,1], e.g. 0.21m for IVA 21%.
public readonly record struct Rate : IComparable<Rate>
{
    public decimal Value { get; }

    public Rate(decimal value)
    {
        if (value is < 0m or > 1m)
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "A rate is between 0 and 1.");
        }

        Value = value;
    }

    public static readonly Rate Zero = new(0m);
    public static readonly Rate One = new(1m);

    public static bool operator <(Rate left, Rate right) => left.Value < right.Value;
    public static bool operator <=(Rate left, Rate right) => left.Value <= right.Value;
    public static bool operator >(Rate left, Rate right) => left.Value > right.Value;
    public static bool operator >=(Rate left, Rate right) => left.Value >= right.Value;

    public int CompareTo(Rate other) => Value.CompareTo(other.Value);

    public override string ToString() => $"{Value:0.####}";
}
