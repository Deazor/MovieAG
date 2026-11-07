using System.Text.Json;

namespace MovieAG.Infrastructure.Sources;

internal static class SourceJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public static int? ParseIntFlexible(JsonElement? value) => value switch
    {
        { ValueKind: JsonValueKind.Number } n when n.TryGetInt32(out var i) => i,
        { ValueKind: JsonValueKind.String } s when int.TryParse(s.GetString(), out var i) => i,
        _ => null
    };

    public static string NormalizeKind(string? kind) => kind?.Trim().ToLowerInvariant() switch
    {
        null or "" => "",
        "anime" => "movie",
        "anime-serial" => "tv",
        var other => other
    };
}
