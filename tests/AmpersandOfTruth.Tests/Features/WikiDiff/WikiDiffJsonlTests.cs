using System.Text.Json;
using System.Text.Json.Nodes;
using AmpersandOfTruth.Web.Features.WikiDiff;

namespace AmpersandOfTruth.Tests.Features.WikiDiff;

/// <summary>The <c>wikidiff batch</c> wire format of CONTRACT.md §9.</summary>
public class WikiDiffJsonlTests
{
    [Fact]
    public void Batch_writes_one_result_per_line_in_order_and_survives_bad_lines()
    {
        var input = string.Join("\n",
            """{"id": "1256:130", "old": "Sota alkoi.", "new": "Sota alkoi 1939."}""",
            "this is not json",
            "",
            """{"id": 7, "old": null, "new": "Uusi sivu."}""",
            """{"id": "x", "new": null}""",
            """{"id": "y", "old": 5, "new": "z"}""",
            "[1, 2]");
        var output = new StringWriter();

        int written = WikiDiffJsonl.Run(new StringReader(input), output);

        var lines = output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => JsonNode.Parse(l)!).ToList();
        Assert.Equal(6, written);
        Assert.Equal(6, lines.Count);
        Assert.Equal("1256:130", (string)lines[0]["id"]!);
        Assert.Equal("1939", (string)lines[0]["added_prose"]!);
        Assert.Null(lines[1]["id"]);
        Assert.StartsWith("not JSON", (string)lines[1]["error"]!);
        Assert.Equal(7, (int)lines[2]["id"]!);
        Assert.Equal("Uusi sivu.", (string)lines[2]["added_prose"]!);
        Assert.Contains("'new' must be a string", (string)lines[3]["error"]!);
        Assert.Contains("'old' must be a string or null", (string)lines[4]["error"]!);
        Assert.Equal("a request is a JSON object", (string)lines[5]["error"]!);
        Assert.All(lines, l => Assert.Equal(1, (int)l["contract"]!));
    }

    [Fact]
    public void Result_uses_the_contract_field_names()
    {
        var line = WikiDiffJsonl.ProcessLine(JsonSerializer.Serialize(new
        {
            id = "a",
            old = "Teksti.\n\n[[Luokka:X]]",
            @new = "Teksti on pidempi.<ref>Lähde</ref>\n\n[[Luokka:X]]",
        }));
        var json = JsonNode.Parse(line)!.AsObject();

        Assert.Equal(["id", "contract", "segments", "added_prose", "removed_prose", "stats"], json.Select(p => p.Key));
        Assert.Equal(["op", "kind", "text", "prose", "old_paragraph", "new_paragraph", "moved"], json["segments"]![0]!.AsObject().Select(p => p.Key));
        Assert.Equal(["added_prose_chars", "removed_prose_chars", "added_refs", "removed_refs", "is_blanking", "is_substantive"],
            json["stats"]!.AsObject().Select(p => p.Key));
        Assert.Equal(["added", "added"], json["segments"]!.AsArray().Select(s => (string)s!["op"]!));
        Assert.Equal(["prose", "reference"], json["segments"]!.AsArray().Select(s => (string)s!["kind"]!));
        Assert.Contains("\"Lähde\"", line);   // UTF-8 as is, not ä
    }

    [Fact]
    public void Research_fixtures_match_their_expected_output()
    {
        // research/29-wikidiff/expected.jsonl is what Python experiments compare against; regenerate it with
        //   scripts/wikidiff.sh batch < research/29-wikidiff/fixtures.jsonl > research/29-wikidiff/expected.jsonl
        var dir = Path.Combine(WikitextDiffTests.RepoRoot(), "research", "29-wikidiff");
        var requests = File.ReadAllLines(Path.Combine(dir, "fixtures.jsonl")).Where(l => l.Length > 0).ToList();
        var expected = File.ReadAllLines(Path.Combine(dir, "expected.jsonl")).Where(l => l.Length > 0).ToList();

        Assert.Equal(requests.Count, expected.Count);
        for (int i = 0; i < requests.Count; i++)
        {
            var actual = JsonNode.Parse(WikiDiffJsonl.ProcessLine(requests[i]));
            Assert.True(JsonNode.DeepEquals(JsonNode.Parse(expected[i]), actual), $"line {i + 1} differs: {actual}");
        }
    }
}
