using System.Text.Json;
using System.Text.Json.Serialization;

namespace Attest.Cli;

internal sealed record AttestConfig(string Provider, string Model, int MaxMutants)
{
    internal const string FileName = "attest.json";
    internal const int DefaultMaxMutants = 200;

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

        if (dto is null || string.IsNullOrWhiteSpace(dto.Provider) || string.IsNullOrWhiteSpace(dto.Model))
            throw new AttestCliException($"'{path}' must specify non-empty \"provider\" and \"model\" fields.");

        return new AttestConfig(dto.Provider, dto.Model, dto.MaxMutants ?? DefaultMaxMutants);
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private sealed record AttestConfigDto(
        [property: JsonPropertyName("provider")] string? Provider,
        [property: JsonPropertyName("model")] string? Model,
        [property: JsonPropertyName("maxMutants")] int? MaxMutants);
}
