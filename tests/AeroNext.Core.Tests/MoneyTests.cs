using AeroNext.Core;
using Xunit;

namespace AeroNext.Core.Tests;

public class MoneyTests
{
    [Fact]
    public void FromDollars_ConvertsToCents()
    {
        Assert.Equal(150, Money.FromDollars(1_500).Cents);
    }

    [Fact]
    public void Zero_IsZeroCents()
    {
        Assert.Equal(0, Money.Zero.Cents);
    }

    [Fact]
    public void Addition_IsExact()
    {
        var a = Money.FromDollars(10);
        var b = Money.FromDollars(5);
        Assert.Equal(Money.FromDollars(15), a + b);
    }

    [Fact]
    public void Subtraction_IsExact()
    {
        var a = Money.FromDollars(10);
        var b = Money.FromDollars(3);
        Assert.Equal(Money.FromDollars(7), a - b);
    }

    [Fact]
    public void Subtraction_CanGoNegative()
    {
        var a = Money.FromDollars(3);
        var b = Money.FromDollars(10);
        Assert.Equal(Money.FromDollars(-7), a - b);
    }

    [Fact]
    public void UnaryNegation_FlipsSign()
    {
        var a = Money.FromDollars(10);
        Assert.Equal(Money.FromDollars(-10), -a);
    }

    [Fact]
    public void Multiplication_ScalesByFactor()
    {
        var a = Money.FromDollars(10);
        Assert.Equal(Money.FromDollars(40), a * 4);
    }

    [Theory]
    [InlineData(100, 200, true)]  // a < b
    [InlineData(200, 100, false)]
    [InlineData(100, 100, false)]
    public void LessThan_ComparesCents(long aCents, long bCents, bool expected)
    {
        Assert.Equal(expected, new Money(aCents) < new Money(bCents));
    }

    [Theory]
    [InlineData(200, 100, true)] // a > b
    [InlineData(100, 200, false)]
    [InlineData(100, 100, false)]
    public void GreaterThan_ComparesCents(long aCents, long bCents, bool expected)
    {
        Assert.Equal(expected, new Money(aCents) > new Money(bCents));
    }

    [Theory]
    [InlineData(100, 200, true)]
    [InlineData(200, 100, false)]
    [InlineData(100, 100, true)] // equal counts as <=
    public void LessThanOrEqual_ComparesCents(long aCents, long bCents, bool expected)
    {
        Assert.Equal(expected, new Money(aCents) <= new Money(bCents));
    }

    [Theory]
    [InlineData(200, 100, true)]
    [InlineData(100, 200, false)]
    [InlineData(100, 100, true)] // equal counts as >=
    public void GreaterThanOrEqual_ComparesCents(long aCents, long bCents, bool expected)
    {
        Assert.Equal(expected, new Money(aCents) >= new Money(bCents));
    }

    [Theory]
    [InlineData(150_000, "$1,500.00")]
    [InlineData(105, "$1.05")]
    [InlineData(0, "$0.00")]
    [InlineData(-150_000, "-$1,500.00")]
    // Cents magnitude under a dollar: integer division of the cents count
    // by 100 truncates a small negative toward zero, so the sign has to be
    // tracked separately or it silently disappears (e.g. -50 cents would
    // print as "$0.50" instead of "-$0.50" without that).
    [InlineData(-50, "-$0.50")]
    [InlineData(-5, "-$0.05")]
    public void ToString_FormatsAsDollarsAndCents(long cents, string expected)
    {
        Assert.Equal(expected, new Money(cents).ToString());
    }
}
