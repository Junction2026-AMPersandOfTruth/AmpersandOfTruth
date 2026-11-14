using System.Text.Json;
using System.Text.Json.Serialization;

namespace AmpersandOfTruth.Web.Features.WebSpread;

/// <summary>JSON for web-spread results: snake_case, enums as snake_case strings (the CLI's output and the result store).</summary>
public static class WebSpreadJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // readable Finnish on a terminal
    };
}
