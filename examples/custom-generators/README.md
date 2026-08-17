# Custom generators

FsCheck's default generator builds test inputs by reflection: find a public constructor, fill
its parameters. That breaks on any real domain type shaped like [`Money`](MoneyFixture/Money.cs) —
private constructor, validating static factory. It also breaks on EF-attached entities and
anything else `new`-ed only through a factory. Without an escape hatch, Attest would die on
first contact with a real codebase.

[`MoneyGenerators.cs`](MoneyFixture/MoneyGenerators.cs) is the escape hatch: a static class
whose static member returns `Arbitrary<Money>`. This is not an Attest-specific convention — it
is FsCheck's own `Arbitrary`-provider pattern, the same thing you'd hand-write for FsCheck.Xunit
today.

```csharp
public static class MoneyGenerators
{
    public static Arbitrary<Money> Money() =>
        Arb.From(Gen.Choose(0, 1_000_000).Select(cents => MoneyFixture.Money.Create(cents / 100m)));
}
```

Point `customGeneratorsType` at it in `attest.json` (see [`attest.json.example`](attest.json.example)):

```json
{
  "customGeneratorsType": "MoneyFixture.MoneyGenerators"
}
```

Attest wires this into the generated test class as
`[Properties(Arbitrary = [typeof(MoneyFixture.MoneyGenerators)])]` — no bespoke registration
mechanism invented, just the attribute you would write by hand.

## This is a proven claim, not a documented intention

Registering the generator changes real behavior, not just whether the code compiles: without it,
FsCheck's reflection-based default cannot construct a `Money` at all, so validation genuinely
fails; with it, the same candidate genuinely validates. Both directions are exercised end to end,
against real generated projects, in
[`SynthesizeAsync_DomainTypeWithNoPublicConstructor_ValidatesOnlyWithACustomGeneratorRegistered`](../../tests/Attest.IntegrationTests/SynthesizerTests.cs).

Run it yourself:

```bash
dotnet test tests/Attest.IntegrationTests --filter "FullyQualifiedName~SynthesizeAsync_DomainTypeWithNoPublicConstructor"
```

## One naming gotcha

The generator method above is named `Money`, matching the type it builds — readable, but not
required (FsCheck matches by the `Arbitrary<T>` return type, not by name). It does mean that
*inside that method's own body*, the bare identifier `Money` resolves to the method itself, not
the type, and `Money.Create(...)` fails to compile with `CS0119`. `MoneyGenerators.cs` qualifies
the call as `MoneyFixture.Money.Create(...)` to avoid it — worth knowing before you copy this
pattern for your own type.

## Trying this against your own project

1. Write your generator class following the shape above: a static class, a static member per
   type you need, returning `Arbitrary<T>`.
2. Add `"customGeneratorsType": "Your.Namespace.YourGenerators"` to your `attest.json`.
3. Run `attest --diff <base-ref> --project path/to/YourProject.csproj` as usual. Attest picks the
   generator up automatically for any candidate that needs it — there is nothing else to wire up.
