using System.Text.Json;
using AmpersandOfTruth.Tests.Features.WikiDiff;
using AmpersandOfTruth.Web.Features.Investigations.Pipeline;
using AmpersandOfTruth.Web.Features.WikiDiff;

namespace AmpersandOfTruth.Tests.Features.Investigations;

/// <summary>P1.S8's citation detectors as ported to C# (#35). The parity cases are the Python detectors' own answers.</summary>
public class SourceChangeDetectorTests
{
    public sealed record Signal(string Label, string Evidence);

    public sealed record Segment(string Op, string Kind, string Text, string? Prose, int? OldParagraph, int? NewParagraph, bool Moved);

    public sealed record Case(string Name, string Old, bool IsBlanking, Segment[] Segments, Signal[] Removed, Signal[] Swapped)
    {
        public override string ToString() => Name;
    }

    public static TheoryData<Case> Parity()
    {
        var path = Path.Combine(WikitextDiffTests.RepoRoot(), "tests", "AmpersandOfTruth.Tests", "Features", "Investigations", "Fixtures", "s8-parity.jsonl");
        var data = new TheoryData<Case>();
        foreach (var line in File.ReadLines(path).Where(l => l.Length > 0))
            data.Add(JsonSerializer.Deserialize<Case>(line, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower })!);
        return data;
    }

    [Theory]
    [MemberData(nameof(Parity))]
    public void Matches_the_python_detectors(Case c)
    {
        var segments = c.Segments
            .Select(s => new DiffSegment(
                Enum.Parse<DiffOp>(s.Op, ignoreCase: true), Enum.Parse<SegmentKind>(s.Kind, ignoreCase: true),
                s.Text, s.Prose ?? "", s.OldParagraph, s.NewParagraph, s.Moved))
            .ToList();
        var diff = new WikitextDiffResult(segments, "", "", new DiffStats(0, 0, 0, 0, c.IsBlanking, IsSubstantive: true));

        var found = SourceChangeDetector.Detect(c.Old, diff);

        Assert.Equal(c.Removed, Signals(found, SourceChangeKind.CitationRemoved));
        Assert.Equal(c.Swapped, Signals(found, SourceChangeKind.CitationSwapped));
    }

    private static Signal[] Signals(IReadOnlyList<SourceChange> found, SourceChangeKind kind) =>
        [.. found.Where(f => f.Kind == kind).Select(f => new Signal(f.Label, f.Evidence))];

    private const string Claim = "Talvisota alkoi 30. marraskuuta 1939, kun Neuvostoliitto hyökkäsi Suomeen.";
    private const string Yle = "<ref>{{Verkkoviite | Osoite = https://yle.fi/a/123 | Nimeke = Arkistolähde}}</ref>";

    private static IReadOnlyList<SourceChange> Detect(string before, string after) =>
        SourceChangeDetector.Detect(before, WikitextDiff.Compare(before, after));

    [Fact]
    public void A_citation_dropped_from_a_sentence_that_stays_is_a_removal()
    {
        var change = Assert.Single(Detect(Claim + Yle, Claim.Replace("Neuvostoliitto hyökkäsi Suomeen", "Suomi hyökkäsi ensin")));

        Assert.Equal(SourceChangeKind.CitationRemoved, change.Kind);
        Assert.Equal("Removed citation (yle.fi) while keeping or altering text", change.Label);
        Assert.Equal(Yle, change.Evidence);
    }

    [Fact]
    public void A_citation_replaced_by_another_source_is_a_swap_and_names_both()
    {
        var change = Assert.Single(Detect(Claim + Yle, Claim + "<ref>Hurtti-Ukko 1941</ref>"));

        Assert.Equal(SourceChangeKind.CitationSwapped, change.Kind);
        Assert.Equal("Citation replaced: yle.fi (Arkistolähde) → Hurtti-Ukko 1941", change.Label);
        Assert.Equal("<ref>Hurtti-Ukko 1941</ref>", change.Evidence);
    }

    [Theory]
    [InlineData(Claim + Yle, Claim + Yle + " Lisätty lause.")]                                        // citations untouched
    [InlineData(Claim + Yle + "\n\nToinen kappale.", "Toinen kappale.")]                               // the paragraph went with it
    [InlineData(Claim + Yle, Claim + "<ref>{{Verkkoviite|Osoite=https://yle.fi/a/123|Nimeke=Arkistolähde|Viitattu=1.2.2020}}</ref>")]   // reformatted
    [InlineData("", Claim + Yle)]                                                                      // only added
    public void Ordinary_edits_are_not_source_changes(string before, string after) =>
        Assert.Empty(Detect(before, after));

    [Fact]
    public void Evidence_is_capped_at_200_characters()
    {
        var longRef = "<ref>" + string.Concat(Enumerable.Repeat("Arkistolähde ja sivuviittaus ", 12)) + "</ref>";

        var change = Assert.Single(Detect(Claim + longRef, Claim));

        Assert.Equal(200, change.Evidence.Length);
        Assert.StartsWith(change.Evidence, longRef);
    }
}
