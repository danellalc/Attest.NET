namespace MoneyFixture;

/// <summary>
/// The shape that breaks FsCheck's reflection-based default generator: no public constructor,
/// only a validating static factory. Common for any real domain type (and for EF-attached
/// entities), and exactly why Attest's custom-generator escape hatch exists.
/// </summary>
public sealed class Money
{
    public decimal Amount { get; }

    private Money(decimal amount) => Amount = amount;

    public static Money Create(decimal amount)
    {
        if (amount < 0)
            throw new ArgumentOutOfRangeException(nameof(amount), "Money cannot be negative.");

        return new Money(amount);
    }
}
