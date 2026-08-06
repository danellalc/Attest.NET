using System.Text.Json.Serialization;

namespace Attest.NET;

internal sealed record StrykerReport(
    [property: JsonPropertyName("files")] Dictionary<string, StrykerFile> Files);

internal sealed record StrykerFile(
    [property: JsonPropertyName("mutants")] List<StrykerMutant> Mutants);

internal sealed record StrykerMutant(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("mutatorName")] string MutatorName,
    [property: JsonPropertyName("replacement")] string Replacement,
    [property: JsonPropertyName("location")] StrykerLocation Location,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("statusReason")] string? StatusReason);

internal sealed record StrykerLocation(
    [property: JsonPropertyName("start")] StrykerPosition Start,
    [property: JsonPropertyName("end")] StrykerPosition End);

internal sealed record StrykerPosition(
    [property: JsonPropertyName("line")] int Line,
    [property: JsonPropertyName("column")] int Column);
