using System.Diagnostics;
using System.Text;
using AmpersandOfTruth.Web.Features.WikiDiff;

namespace AmpersandOfTruth.Tests.Features.WikiDiff;

/// <summary>The #29 fixture list: Fixtures/base.txt is the parent revision, Fixtures/&lt;case&gt;.new.txt the edit.</summary>
public class WikitextDiffTests
{
    private static readonly string Base = Fixture("base");

    internal static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(RepoRoot(), "tests", "AmpersandOfTruth.Tests", "Features", "WikiDiff", "Fixtures", $"{name}.txt"), Encoding.UTF8);

    internal static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AmpersandOfTruth.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repo root (AmpersandOfTruth.sln) not found");
    }

    private static WikitextDiffResult Edit(string name) => WikitextDiff.Compare(Base, Fixture($"{name}.new"));

    private static IEnumerable<DiffSegment> Changed(WikitextDiffResult r) => r.Segments.Where(s => !s.Moved);

    [Fact]
    public void Small_insertion_is_one_added_prose_segment_inside_its_paragraph()
    {
        var r = Edit("small-insertion");

        var s = Assert.Single(r.Segments);
        Assert.Equal((DiffOp.Added, SegmentKind.Prose), (s.Op, s.Kind));
        Assert.Equal("Sotaan osallistui myös ruotsalaisia vapaaehtoisia.", s.Text);
        Assert.Equal((6, 6), (s.OldParagraph, s.NewParagraph));   // an edit inside an existing paragraph has both
        Assert.Equal(s.Prose, r.AddedProse);
        Assert.Equal("", r.RemovedProse);
        Assert.Equal(new DiffStats(50, 0, 0, 0, IsBlanking: false, IsSubstantive: true), r.Stats);
    }

    [Fact]
    public void Paragraph_rewrite_reports_only_the_changed_words()
    {
        var r = Edit("paragraph-rewrite");

        Assert.Equal("vaati … Suomelta alueluovutuksia … vaatimukset neuvottelujen jälkeen", r.RemovedProse);
        Assert.Equal("ehdotti … Suomelle alueiden vaihtoa … kohtuulliset ehdotukset, mikä johti sotaan", r.AddedProse);
        Assert.All(r.Segments, s => Assert.Equal((3, 3), (s.OldParagraph, s.NewParagraph)));
        Assert.DoesNotContain(r.Segments, s => s.Text.Contains("Karjalankann"));
        Assert.True(r.Stats.IsSubstantive);
    }

    [Fact]
    public void Moved_section_is_reported_once_as_moved_and_is_not_substantive()
    {
        var r = Edit("moved-section");

        Assert.NotEmpty(r.Segments);
        Assert.All(r.Segments, s => Assert.True(s.Moved));
        Assert.All(r.Segments, s => Assert.NotEqual(s.OldParagraph, s.NewParagraph));
        Assert.Contains(r.Segments, s => s.Text == "== Seuraukset ==" || s.Text == "== Sodan kulku ==");
        Assert.Equal("", r.AddedProse + r.RemovedProse);
        Assert.Equal(new DiffStats(0, 0, 0, 0, false, false), r.Stats);
    }

    [Fact]
    public void Reference_swap_is_one_removed_and_one_added_reference_with_its_citation_text()
    {
        var r = Edit("reference-swap");

        Assert.Collection(r.Segments,
            removed =>
            {
                Assert.Equal((DiffOp.Removed, SegmentKind.Reference), (removed.Op, removed.Kind));
                Assert.StartsWith("<ref>{{Lehtiviite", removed.Text);
                Assert.Equal("Esimerkki, Erkki; Evakot ja asutus; Historiallinen Aikakauskirja; 2005; 2", removed.Prose);
            },
            added =>
            {
                Assert.Equal((DiffOp.Added, SegmentKind.Reference), (added.Op, added.Kind));
                Assert.EndsWith("</ref>", added.Text);
                Assert.Equal("https://propaganda.example.org/evakot; Totuus evakoista; 2.11.2026", added.Prose);
            });
        Assert.Equal("", r.AddedProse);   // citations are counted, not mixed into the prose
        Assert.Equal(new DiffStats(0, 0, AddedRefs: 1, RemovedRefs: 1, IsBlanking: false, IsSubstantive: true), r.Stats);
    }

    [Fact]
    public void Template_only_change_is_not_substantive()
    {
        var r = Edit("template-only");

        Assert.Equal(["{{Lähteetön|päiväys=lokakuu 2026}}", "{{Lähde}}"], r.Segments.Select(s => s.Text));
        Assert.All(r.Segments, s => Assert.Equal((DiffOp.Added, SegmentKind.Template, ""), (s.Op, s.Kind, s.Prose)));
        Assert.Null(r.Segments[0].OldParagraph);   // a new block before the infobox
        Assert.False(r.Stats.IsSubstantive);
    }

    [Fact]
    public void Infobox_value_change_is_a_substantive_template_segment()
    {
        var r = Edit("infobox-value");

        Assert.Equal(["25", "250"], r.Segments.Select(s => s.Text));
        Assert.All(r.Segments, s => Assert.Equal((SegmentKind.Template, 0, 0), (s.Kind, s.OldParagraph, s.NewParagraph)));
        Assert.Equal(("250", "25"), (r.AddedProse, r.RemovedProse));
        Assert.True(r.Stats.IsSubstantive);
    }

    [Fact]
    public void Page_blanking_is_blanking()
    {
        var r = Edit("page-blanking");

        Assert.True(r.Stats.IsBlanking);
        Assert.Equal("TALVISOTAA EI OLLUT", r.AddedProse);
        Assert.Equal(4, r.Stats.RemovedRefs);   // three citations and one reuse of a named ref
        Assert.True(WikitextDiff.Compare(Base, "").Stats.IsBlanking);
        Assert.True(WikitextDiff.Compare(Base, "  \n").Stats.IsBlanking);
    }

    [Fact]
    public void Section_blanking_is_blanking_but_a_rewrite_or_a_small_removal_is_not()
    {
        var r = Edit("section-blanking");
        Assert.True(r.Stats.IsBlanking);
        Assert.All(r.Segments, s => Assert.Equal(DiffOp.Removed, s.Op));
        Assert.All(r.Segments, s => Assert.Null(s.NewParagraph));

        Assert.False(WikitextDiff.Compare(Fixture("section-blanking.new"), Base).Stats.IsBlanking);   // putting it back
        Assert.False(Edit("paragraph-rewrite").Stats.IsBlanking);
        Assert.False(WikitextDiff.Compare(Fixture("small-insertion.new"), Base).Stats.IsBlanking);   // removing one sentence
    }

    [Fact]
    public void First_revision_is_diffed_against_empty()
    {
        var r = WikitextDiff.Compare(null, Fixture("first-revision.new"));

        Assert.All(r.Segments, s => Assert.Equal((DiffOp.Added, (int?)null), (s.Op, s.OldParagraph)));
        Assert.Equal([SegmentKind.Prose, SegmentKind.Category], r.Segments.Select(s => s.Kind));
        Assert.Equal("Talvisota oli Suomen ja Neuvostoliiton välinen sota vuosina 1939–1940.", r.AddedProse);
        Assert.Equal(new DiffStats(70, 0, 0, 0, false, true), r.Stats);
        Assert.Equal(r, WikitextDiff.Compare("", Fixture("first-revision.new")), new ResultComparer());
    }

    [Fact]
    public void Category_and_interwiki_only_change_is_not_substantive()
    {
        var r = Edit("category-interwiki-only");

        Assert.Equal([(DiffOp.Added, SegmentKind.Category), (DiffOp.Removed, SegmentKind.Interwiki)], r.Segments.Select(s => (s.Op, s.Kind)));
        Assert.Equal(["[[Luokka:Toinen maailmansota]]", "[[sv:Vinterkriget]]"], r.Segments.Select(s => s.Text));
        Assert.False(r.Stats.IsSubstantive);
    }

    [Fact]
    public void Finnish_words_with_diacritics_stay_whole()
    {
        var r = Edit("finnish-name-swap");

        Assert.Equal([(DiffOp.Removed, "Risto Ryti"), (DiffOp.Added, "Väinö Tanner")], r.Segments.Select(s => (s.Op, s.Text)));
        Assert.Equal(("Väinö Tanner", "Risto Ryti"), (r.AddedProse, r.RemovedProse));
    }

    [Fact]
    public void Markup_only_change_is_not_substantive()
    {
        var r = Edit("markup-only");

        Assert.Equal(["[[", "]]", "'''", "'''"], r.Segments.Select(s => s.Text));
        Assert.All(r.Segments, s => Assert.Equal(SegmentKind.Markup, s.Kind));
        Assert.False(r.Stats.IsSubstantive);
    }

    [Fact]
    public void Whitespace_only_change_has_no_segments()
    {
        Assert.Empty(Edit("whitespace-only").Segments);
        Assert.Empty(WikitextDiff.Compare(Base, Base.Replace("\n", "\r\n")).Segments);
        Assert.Empty(WikitextDiff.Compare(Base, Base).Segments);
    }

    [Fact]
    public void A_revert_is_the_mirror_image_of_the_edit()
    {
        var edit = Edit("small-insertion");
        var revert = WikitextDiff.Compare(Fixture("small-insertion.new"), Base);

        var s = Assert.Single(revert.Segments);
        Assert.Equal((DiffOp.Removed, edit.Segments[0].Text), (s.Op, s.Text));
        Assert.Equal(edit.AddedProse, revert.RemovedProse);
    }

    public static TheoryData<string> Cases => new(
        "small-insertion", "paragraph-rewrite", "moved-section", "reference-swap", "template-only", "infobox-value", "page-blanking",
        "section-blanking", "category-interwiki-only", "finnish-name-swap", "markup-only", "whitespace-only", "first-revision");

    [Theory]
    [MemberData(nameof(Cases))]
    public void Every_segment_is_an_exact_substring_of_its_revision(string name)
    {
        var newText = Fixture($"{name}.new");
        var r = WikitextDiff.Compare(Base, newText);

        foreach (var s in r.Segments)
        {
            Assert.Contains(s.Text, s.Op == DiffOp.Added ? newText : Base);
            Assert.Equal(s.Text.Trim(), s.Text);
            Assert.NotEqual("", s.Text);
        }
    }

    // ── Size: the profile says median 3.8 KB, p99 52 KB, max 199 KB ─────────────────────────────────────────────────

    internal static string HugeArticle(int paragraphs, int seed = 7)
    {
        var rng = new Random(seed);
        string[] words = ["sota", "Suomi", "Neuvostoliitto", "rauha", "armeija", "hyökkäys", "puolustus", "Karjala", "vuonna", "joka",
            "oli", "ja", "mutta", "pääministeri", "hallitus", "rintama", "evakko", "Viipuri", "talvi", "kesä"];
        var sb = new StringBuilder("{{Infobox sota\n| nimi = Iso artikkeli\n| alku = 1939\n}}\n");
        for (int p = 0; p < paragraphs; p++)
        {
            if (p % 8 == 0) sb.Append($"\n== Osa {p / 8} ==\n");
            sb.Append($"Kappale {p}: ");
            for (int w = 0; w < 18; w++) sb.Append(words[rng.Next(words.Length)]).Append(w % 6 == 5 ? ". " : " ");
            sb.Append($"[[Linkki {p}|linkki]].<ref>{{{{Verkkoviite | Osoite = https://example.fi/{p} | Nimeke = Lähde {p}}}}}</ref>\n\n");
        }
        sb.Append("[[Luokka:Testi]]\n");
        return sb.ToString();
    }

    [Fact]
    public void Huge_article_diffs_within_budget()
    {
        var old = HugeArticle(900);
        Assert.InRange(old.Length, 150_000, 250_000);
        var middle = old.IndexOf("Kappale 450:", StringComparison.Ordinal);
        var edited = old.Insert(middle, "Täysin uusi väite lisättiin tähän. ");

        WikitextDiff.Compare(old, edited);   // warm-up (JIT)
        var clock = Stopwatch.StartNew();
        var r = WikitextDiff.Compare(old, edited);
        var small = clock.Elapsed;

        var s = Assert.Single(r.Segments);
        Assert.Equal("Täysin uusi väite lisättiin tähän.", s.Text);

        clock.Restart();
        var blank = WikitextDiff.Compare(old, "PERTTI ON TYHMÄ");
        var blanking = clock.Elapsed;
        Assert.True(blank.Stats.IsBlanking);

        clock.Restart();
        var rewrite = WikitextDiff.Compare(old, HugeArticle(900, seed: 8));
        var rewritten = clock.Elapsed;
        Assert.True(rewrite.Stats.IsSubstantive);

        // Generous bounds for a shared CI box; the measured numbers are in research/29-wikidiff/README.md.
        Assert.True(small < TimeSpan.FromMilliseconds(500), $"small edit took {small.TotalMilliseconds} ms");
        Assert.True(blanking < TimeSpan.FromMilliseconds(500), $"blanking took {blanking.TotalMilliseconds} ms");
        Assert.True(rewritten < TimeSpan.FromMilliseconds(2000), $"full rewrite took {rewritten.TotalMilliseconds} ms");
    }

    private sealed class ResultComparer : IEqualityComparer<WikitextDiffResult>
    {
        public bool Equals(WikitextDiffResult? x, WikitextDiffResult? y) =>
            x is not null && y is not null && x.Segments.SequenceEqual(y.Segments) && x.AddedProse == y.AddedProse &&
            x.RemovedProse == y.RemovedProse && x.Stats == y.Stats;

        public int GetHashCode(WikitextDiffResult obj) => obj.Stats.GetHashCode();
    }
}
