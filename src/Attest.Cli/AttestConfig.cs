using System.Text.Json;
using System.Text.Json.Serialization;

namespace Attest.Cli;

internal sealed record AttestConfig(
    string Provider,
    string Model,
    int MaxMutants,
    string? BaseUrl,
    string JsonMode,
    decimal? InputPricePerMillion,
    decimal? OutputPricePerMillion,
    string? CustomGeneratorsType = null)
{
    internal const string FileName = "attest.json";
    internal const int DefaultMaxMutants = 200;
    internal const string DefaultJsonMode = "object";

    internal static AttestConfig Load(string repositoryRoot)
    {
        var path = Path.Combine(repositoryRoot, FileName);
        if (!File.Exists(path))
            throw new AttestCliException(
                $"No {FileName} found at '{path}'. Create one with at least {{\"provider\": \"anthropic\", \"model\": \"claude-sonnet-5\"}}.");

        AttestConfigDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<AttestConfigDto>(File.ReadAllText(path), JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new AttestCliException($"'{path}' is not valid JSON: {ex.Message}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new AttestCliException($"Could not read '{path}': {ex.Message}");
        }

        if (dto is null || string.IsNullOrWhiteSpace(dto.Provider) || string.IsNullOrWhiteSpace(dto.Model))
            throw new AttestCliException($"'{path}' must specify non-empty \"provider\" and \"model\" fields.");

        // A hand-edited "maxMutants": 0 (or a negative value) trips Falsifier.VerifyCeiling on
        // the very first tested mutant, aborting with a message that gives no hint the actual
        // root cause is this config value; init's own interactive prompt already falls back to
        // the default for the same input, so Load does too instead of trusting it verbatim.
        var maxMutants = dto.MaxMutants is > 0 ? dto.MaxMutants.Value : DefaultMaxMutants;
        var jsonMode = string.IsNullOrWhiteSpace(dto.JsonMode) ? DefaultJsonMode : dto.JsonMode;

        return new AttestConfig(
            dto.Provider, dto.Model, maxMutants, dto.BaseUrl, jsonMode,
            dto.InputPricePerMillion, dto.OutputPricePerMillion, dto.CustomGeneratorsType);
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    internal sealed record AttestConfigDto(
        [property: JsonPropertyName("provider")] string? Provider,
        [property: JsonPropertyName("model")] string? Model,
        [property: JsonPropertyName("maxMutants")] int? MaxMutants,
        [property: JsonPropertyName("baseUrl")] string? BaseUrl = null,
        [property: JsonPropertyName("jsonMode")] string? JsonMode = null,
        [property: JsonPropertyName("inputPricePerMillion")] decimal? InputPricePerMillion = null,
        [property: JsonPropertyName("outputPricePerMillion")] decimal? OutputPricePerMillion = null,
        [property: JsonPropertyName("customGeneratorsType")] string? CustomGeneratorsType = null);
}
