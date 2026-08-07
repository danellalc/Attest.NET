namespace PriceCalculatorFixture;

public static class PriceCalculator
{
    public static decimal ApplyDiscount(decimal originalPrice, decimal discountPercent)
    {
        if (discountPercent < 0 || discountPercent > 100)
            throw new ArgumentOutOfRangeException(nameof(discountPercent));

        if (originalPrice < 0)
            throw new ArgumentOutOfRangeException(nameof(originalPrice));

        var discount = originalPrice * discountPercent / 100;
        return originalPrice - discount;
    }
}
