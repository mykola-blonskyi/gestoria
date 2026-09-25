namespace GestorIA.Engine;

public readonly record struct YearMonth
{
    public int Year { get; }
    public int Month { get; }

    public YearMonth(int year, int month)
    {
        if (month is < 1 or > 12)
        {
            throw new ArgumentOutOfRangeException(nameof(month), month, "A month is 1..12.");
        }

        Year = year;
        Month = month;
    }

    public static YearMonth Of(DateOnly date) => new(date.Year, date.Month);

    public int MonthsSince(YearMonth other) => (Year * 12 + Month) - (other.Year * 12 + other.Month);

    public override string ToString() => $"{Year:D4}-{Month:D2}";
}