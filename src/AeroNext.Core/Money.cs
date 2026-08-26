namespace AeroNext.Core;

/// <summary>
/// Currency as integer cents. Never use float/double for money — see
/// determinism rules in CLAUDE.md.
/// </summary>
public readonly record struct Money(long Cents)
{
    public static readonly Money Zero = new(0);

    public static Money FromDollars(long dollars) => new(dollars * 100);

    public static Money operator +(Money a, Money b) => new(a.Cents + b.Cents);
    public static Money operator -(Money a, Money b) => new(a.Cents - b.Cents);
    public static Money operator -(Money a) => new(-a.Cents);
    public static Money operator *(Money a, long factor) => new(a.Cents * factor);

    public static bool operator <(Money a, Money b) => a.Cents < b.Cents;
    public static bool operator >(Money a, Money b) => a.Cents > b.Cents;
    public static bool operator <=(Money a, Money b) => a.Cents <= b.Cents;
    public static bool operator >=(Money a, Money b) => a.Cents >= b.Cents;

    public override string ToString()
    {
        var sign = Cents < 0 ? "-" : "";
        var absCents = Math.Abs(Cents);
        return $"{sign}${absCents / 100:N0}.{absCents % 100:D2}";
    }
}
