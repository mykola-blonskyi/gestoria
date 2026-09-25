using System.Globalization;

namespace GestorIA.Domain.ValueObjects;

// SPEC-001 §2, ADR-0004: decimal only, currency fixed to EUR, rounding always explicit.
public readonly record struct Money : IComparable<Money>
{
    public const string Currency = "EUR";

    public decimal Amount { get; }

    public Money(decimal amount)
    {
        Amount = amount;
    }

    public static readonly Money Zero = new(0m);

    public Money Round2() => new(Math.Round(Amount, 2, MidpointRounding.AwayFromZero));

    public static Money operator +(Money left, Money right) => new(left.Amount + right.Amount);
    public static Money operator -(Money left, Money right) => new(left.Amount - right.Amount);
    public static Money operator -(Money value) => new(-value.Amount);
    public static Money operator *(Money money, Rate rate) => new(money.Amount * rate.Value);

    public static bool operator <(Money left, Money right) => left.Amount < right.Amount;
    public static bool operator <=(Money left, Money right) => left.Amount <= right.Amount;
    public static bool operator >(Money left, Money right) => left.Amount > right.Amount;
    public static bool operator >=(Money left, Money right) => left.Amount >= right.Amount;

    public int CompareTo(Money other) => Amount.CompareTo(other.Amount);

    public override string ToString() => $"{Amount.ToString(CultureInfo.InvariantCulture)} {Currency}";
}
