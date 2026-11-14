using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AmpersandOfTruth.Web.Features.WikiDiff;

/// <summary>
/// The <c>wikidiff batch</c> wire format (CONTRACT.md §9): one request per line in, one result per line out, same order.
/// Request: <c>{"id": …, "old": "&lt;wikitext&gt;" | null, "new": "&lt;wikitext&gt;"}</c>. A line that cannot be diffed yields
/// <c>{"id": …, "contract": 1, "error": "…"}</c> and the batch goes on. Blank input lines are skipped.
/// </summary>
public static class WikiDiffJsonl
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower, allowIntegerValues: false) },
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,   // keep ä/ö readable; this JSON is never embedded in HTML
    };

    public static readonly JsonSerializerOptions IndentedOptions = new(Options) { WriteIndented = true };

    private sealed record Result(
        JsonElement? Id, int Contract, IReadOnlyList<DiffSegment> Segments, string AddedProse, string RemovedProse, DiffStats Stats);

    private sealed record Failure(JsonElement? Id, int Contract, string Error);

    /// <summary>Diffs every line of <paramref name="input"/> into <paramref name="output"/>. Returns the number of results written.</summary>
    public static int Run(TextReader input, TextWriter output)
    {
        int written = 0;
        while (input.ReadLine() is { } line)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            output.WriteLine(ProcessLine(line));
            written++;
        }
        output.Flush();
        return written;
    }

    public static string ProcessLine(string line)
    {
        JsonElement? id = null;
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return Fail(null, "a request is a JSON object");
            if (root.TryGetProperty("id", out var idElement)) id = idElement.Clone();

            string? oldText = null;
            if (root.TryGetProperty("old", out var oldElement))
            {
                if (oldElement.ValueKind == JsonValueKind.String) oldText = oldElement.GetString();
                else if (oldElement.ValueKind != JsonValueKind.Null) return Fail(id, "'old' must be a string or null");
            }
            if (!root.TryGetProperty("new", out var newElement) || newElement.ValueKind != JsonValueKind.String)
                return Fail(id, "'new' must be a string (a suppressed revision has no text to diff)");

            return Serialize(id, WikitextDiff.Compare(oldText, newElement.GetString()!));
        }
        catch (JsonException ex)
        {
            return Fail(id, $"not JSON: {ex.Message}");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return Fail(id, $"diff failed: {ex.GetType().Name}: {ex.Message}");   // one bad pair must not stop a batch
        }
    }

    public static string Serialize(JsonElement? id, WikitextDiffResult r, bool indented = false) =>
        JsonSerializer.Serialize(new Result(id, WikitextDiff.Contract, r.Segments, r.AddedProse, r.RemovedProse, r.Stats),
            indented ? IndentedOptions : Options);

    private static string Fail(JsonElement? id, string error) =>
        JsonSerializer.Serialize(new Failure(id, WikitextDiff.Contract, error), Options);
}
