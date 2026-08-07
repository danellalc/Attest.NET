namespace Attest.Core;

/// <summary>
/// Raised by a provider when asked to use a model it has no published pricing for. Refusing
/// beats silently reporting a wrong cost: model deprecation and new releases are a named risk,
/// not something to guess through.
/// </summary>
public sealed class AttestUnknownModelPricingException : AttestException
{
    /// <summary>The model identifier no pricing entry was found for.</summary>
    public string Model { get; }

    /// <param name="model">The model identifier no pricing entry was found for.</param>
    public AttestUnknownModelPricingException(string model)
        : base($"No known pricing for model '{model}'. Use a recognized model or update the provider's pricing table.")
    {
        Model = model;
    }
}
