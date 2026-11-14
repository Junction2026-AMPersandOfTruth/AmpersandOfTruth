using System.Diagnostics;
using System.Text.Json.Nodes;
using AmpersandOfTruth.Web.Features.WikiDiff;

namespace AmpersandOfTruth.Tests.Features.WikiDiff;

/// <summary>#91: pathological nesting from vandal edits must not overflow the stack or lose a batch.</summary>
public class DeepNestingTests
{
    private const string Base = "Talvisota alkoi 30. marraskuuta 1939.\n\nToinen kappale tekstiä tässä.\n";

    public static TheoryData<string, string, string, int> Nestings() => new()
    {
        { "{{a|", "b", "}}", 20_000 },
        { "{{a|", "b", "}}", 100_000 },
        { "[[a|", "b", "]]", 20_000 },
        { "[[a|", "b", "]]", 100_000 },
        { "[[", "b", "]]", 100_000 },
        { "[http://x.fi ", "b", "]", 20_000 },
        { "<ref>{{a|", "b", "}}</ref>", 20_000 },
        { "{{", "b", "", 100_000 },            // never closed
        { "<ref>", "b", "", 50_000 },          // never closed
        { "!!", "b", "", 100_000 },            // one long line of table-cell markers
    };

    [Theory]
    [MemberData(nameof(Nestings))]
    public void Deep_nesting_returns_a_result_within_budget(string open, string middle, string close, int depth)
    {
        var deep = Base + "x " + string.Concat(Enumerable.Repeat(open, depth)) + middle + string.Concat(Enumerable.Repeat(close, depth)) + "\n";
        var clock = Stopwatch.StartNew();

        var added = WikitextDiff.Compare(Base, deep);
        var removed = WikitextDiff.Compare(deep, Base);
        var prose = WikitextProse.Of(deep);

        Assert.NotEmpty(added.Segments);
        Assert.NotEmpty(removed.Segments);
        Assert.StartsWith("Talvisota alkoi", prose.Prose);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5), $"{depth} × {open} took {clock.Elapsed.TotalMilliseconds} ms");
    }

    [Fact]
    public void Nesting_below_the_cap_is_still_parsed_as_templates()
    {
        int depth = WikitextTokenizer.MaxNesting - 1;
        var nested = "x " + string.Concat(Enumerable.Repeat("{{a|", depth)) + "b" + string.Concat(Enumerable.Repeat("}}", depth));
        var r = WikitextDiff.Compare("x", nested);
        Assert.All(r.Segments, s => Assert.Equal(SegmentKind.Template, s.Kind));
        Assert.False(r.Stats.IsSubstantive);   // an inline template changes no prose
    }

    [Fact]
    public void A_batch_answers_every_line_around_a_pathological_pair()
    {
        static string Line(string id, string newText) =>
            new JsonObject { ["id"] = id, ["old"] = Base, ["new"] = newText }.ToJsonString();
        var deep = Base + "x " + new string('{', 2 * 100_000) + "b" + new string('}', 2 * 100_000);
        var input = string.Join("\n", Line("ok", Base + "\nLisäys.\n"), Line("deep", deep), Line("after", Base + "\nToinen lisäys.\n"));
        var output = new StringWriter();

        int written = WikiDiffJsonl.Run(new StringReader(input), output);

        var lines = output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => JsonNode.Parse(l)!).ToList();
        Assert.Equal(3, written);
        Assert.Equal(["ok", "deep", "after"], lines.Select(l => (string)l["id"]!));
        Assert.All(lines, l => Assert.Null(l["error"]));
        Assert.Equal("Toinen lisäys.", (string)lines[2]["added_prose"]!);
    }
}
