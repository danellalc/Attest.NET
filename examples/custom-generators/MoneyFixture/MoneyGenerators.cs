namespace MoneyFixture;

using FsCheck;
using FsCheck.Fluent;

/// <summary>
/// FsCheck's own convention for a custom-generator provider: a static class whose static
/// members return <see cref="Arbitrary{T}"/>. Point <c>customGeneratorsType</c> in
/// <c>attest.json</c> at this class's fully qualified name and Attest wires it into the
/// generated test as <c>[Properties(Arbitrary = [typeof(MoneyFixture.MoneyGenerators)])]</c> --
/// no bespoke registration mechanism, this is exactly what you'd write for FsCheck.Xunit today.
/// </summary>
public static class MoneyGenerators
{
    // Fully qualified on purpose: inside a method named Money, the bare identifier "Money"
    // resolves to this method itself, not the type -- MoneyFixture.Money.Create disambiguates.
    public static Arbitrary<Money> Money() =>
        Arb.From(Gen.Choose(0, 1_000_000).Select(cents => MoneyFixture.Money.Create(cents / 100m)));
}
