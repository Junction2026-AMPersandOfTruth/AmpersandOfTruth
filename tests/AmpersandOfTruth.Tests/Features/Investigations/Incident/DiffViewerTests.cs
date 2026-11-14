using AmpersandOfTruth.Tests.Features.Translation;
using AmpersandOfTruth.Web.Features.Evidence;
using AmpersandOfTruth.Web.Features.Investigations.Incident;
using AmpersandOfTruth.Web.Features.Investigations.View;
using AmpersandOfTruth.Web.Features.Translation;
using AmpersandOfTruth.Web.Features.WikiDiff;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace AmpersandOfTruth.Tests.Features.Investigations.Incident;

public sealed class DiffViewerTests : BunitContext
{
    private static readonly string ParagraphBreak = new([(char)10, (char)10]);

    private static DiffPassage MakePassage(string before, string after, long revId = 500, long? parentRevId = 499) =>
        new(revId, parentRevId, before, after, WikitextDiff.Compare(before, after));

    [Fact]
    public void Null_passage_renders_warning_StatusBanner()
    {
        var cut = Render<DiffViewer>(p => p.Add(x => x.Passage, null));

        var banner = cut.Find("[data-testid=status-banner]");
        Assert.Contains("aot-banner--warn", banner.ClassList);
        Assert.Contains("The text of this edit is not available right now.", banner.TextContent);
    }

    [Theory]
    [InlineData(DiffTextMode.Prose)]
    [InlineData(DiffTextMode.Wikitext)]
    public void Hostile_passage_renders_as_encoded_literal_text_in_both_text_modes(DiffTextMode textMode)
    {
        var translator = new FakeEvidenceTranslator();
        Services.AddSingleton<IEvidenceTranslator>(translator);

        const string before = "Talvisota alkoi marraskuussa 1939.";
        const string hostilePayload =
            "Talvisota alkoi marraskuussa 1939. <script>alert(1)</script> <img src=x onerror=alert(1)> {{template}} '''bold''' perkele";

        var passage = MakePassage(before, hostilePayload);

        var cut = Render<DiffViewer>(p => p
            .Add(x => x.Passage, passage)
            .Add(x => x.Text, textMode)
            .Add(x => x.Lang, "fi")
            .Add(x => x.EvidenceQuotes, ["<script>alert(1)</script>"]));

        Assert.Empty(cut.FindAll("script"));
        Assert.Empty(cut.FindAll("img"));
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", cut.Markup);
        Assert.Contains("&lt;img src=x onerror=alert(1)&gt;", cut.Markup);
        Assert.Contains("perkele", cut.Find("[data-testid=diff-body-fi]").TextContent);

        if (textMode == DiffTextMode.Wikitext)
        {
            Assert.Contains("{{template}}", cut.Find("[data-testid=diff-body-fi]").TextContent);
            Assert.Contains("'''bold'''", cut.Find("[data-testid=diff-body-fi]").TextContent);
        }
        else
        {
            Assert.DoesNotContain("{{template}}", cut.Find("[data-testid=diff-body-fi]").TextContent);
            Assert.Contains("bold", cut.Find("[data-testid=diff-body-fi]").TextContent);
        }
    }

    [Fact]
    public void Mode_toggles_raise_OnModeChanged_split_renders_two_columns_fold_expands_and_evidence_is_marked()
    {
        Services.AddSingleton<IEvidenceTranslator>(new FakeEvidenceTranslator());

        var oldParas = new[]
        {
            "Kappale 0.",
            "Kappale 1.",
            "Kappale 2.",
            "Neuvostojoukot ylittivät rajan, kun Neuvostoliitto lavasti Mainilan laukaukset.",
            "Kappale 4.",
        };
        var newParas = new[]
        {
            "Kappale 0.",
            "Kappale 1.",
            "Kappale 2.",
            "Neuvostojoukot ylittivät rajan, kun Suomi tulitti rajakylää 26. marraskuuta.",
            "Kappale 4.",
        };

        var passage = MakePassage(string.Join(ParagraphBreak, oldParas), string.Join(ParagraphBreak, newParas));
        var events = new List<(DiffMode Mode, DiffTextMode Text, string Lang)>();

        var cut = Render<DiffViewer>(p => p
            .Add(x => x.Passage, passage)
            .Add(x => x.Mode, DiffMode.Inline)
            .Add(x => x.Text, DiffTextMode.Prose)
            .Add(x => x.Lang, "fi")
            .Add(x => x.EvidenceQuotes, ["Suomi tulitti rajakylää"])
            .Add(x => x.OnModeChanged, e => events.Add(e)));

        // Evidence quote wrapped in <mark class="aot-ev aot-evidence"> inside <ins>.
        var mark = Assert.Single(cut.FindAll("mark"));
        Assert.Contains("aot-evidence", mark.ClassList);
        Assert.Equal("Suomi tulitti rajakylää", mark.TextContent);
        Assert.NotEmpty(cut.FindAll("ins.aot-diff__add"));
        Assert.NotEmpty(cut.FindAll("del.aot-diff__del"));

        // Fold button collapses paragraphs 0..1 (2 unchanged lines) and expands on click.
        var foldBtn = Assert.Single(cut.FindAll("[data-testid=diff-fold]"));
        Assert.Equal("⋯ 2 unchanged lines", foldBtn.TextContent);
        Assert.DoesNotContain("Kappale 0.", cut.Find("[data-testid=diff-body-fi]").TextContent);
        foldBtn.Click();
        Assert.Empty(cut.FindAll("[data-testid=diff-fold]"));
        Assert.Contains("Kappale 0.", cut.Find("[data-testid=diff-body-fi]").TextContent);

        // Toggles raise OnModeChanged with updated tuple.
        cut.FindAll("[data-testid=diff-view-toggle] button")[1].Click();
        cut.FindAll("[data-testid=diff-text-toggle] button")[1].Click();
        cut.FindAll("[data-testid=diff-lang-toggle] button")[0].Click();

        Assert.Equal(
            [
                (DiffMode.Split, DiffTextMode.Prose, "fi"),
                (DiffMode.Inline, DiffTextMode.Wikitext, "fi"),
                (DiffMode.Inline, DiffTextMode.Prose, "en"),
            ],
            events);

        // Split mode renders two columns (before / after).
        var splitCut = Render<DiffViewer>(p => p
            .Add(x => x.Passage, passage)
            .Add(x => x.Mode, DiffMode.Split)
            .Add(x => x.Text, DiffTextMode.Prose)
            .Add(x => x.Lang, "fi")
            .Add(x => x.EvidenceQuotes, ["Suomi tulitti rajakylää"]));

        var cols = splitCut.FindAll(".aot-diff__col");
        Assert.Equal(2, cols.Count);
        Assert.NotEmpty(cols[0].QuerySelectorAll("del.aot-diff__del"));
        Assert.Empty(cols[0].QuerySelectorAll("ins"));
        Assert.NotEmpty(cols[1].QuerySelectorAll("ins.aot-diff__add"));
        Assert.Empty(cols[1].QuerySelectorAll("del"));
        Assert.Single(cols[1].QuerySelectorAll("mark.aot-evidence"));
    }

    [Fact]
    public void Lang_en_aligned_renders_translated_ins_with_MtLabel_semantics_and_caches_per_passage()
    {
        var translator = new FakeEvidenceTranslator()
            .ReturnsPassage(new AlignedTranslation(
                BeforeEn: "Soviet troops crossed the border after the Soviet Union staged the Mainila shots.",
                AfterEn: "Soviet troops crossed the border after Finland shelled the village of Mainila.",
                ChangedInAfterEn: [(39, 38)],
                ChangedInBeforeEn: [(39, 41)],
                Aligned: true,
                Ok: true));

        Services.AddSingleton<IEvidenceTranslator>(translator);

        var passage = MakePassage(
            "Neuvostojoukot ylittivät rajan, kun Neuvostoliitto lavasti Mainilan laukaukset.",
            "Neuvostojoukot ylittivät rajan, kun Suomi ampui Mainilan kylään.");

        var cut = Render<DiffViewer>(p => p
            .Add(x => x.Passage, passage)
            .Add(x => x.Lang, "en")
            .Add(x => x.Scope, "inv-winter-war"));

        cut.WaitForAssertion(() =>
        {
            var mt = cut.Find("[data-testid=diff-mt]");
            var tag = mt.QuerySelector(".aot-mt__tag");
            Assert.NotNull(tag);
            Assert.Equal("Machine translation", tag.TextContent);
            Assert.Equal(Attribution.MachineTranslationNotice, tag.GetAttribute("title"));

            var ins = Assert.Single(mt.QuerySelectorAll("ins.aot-diff__add"));
            Assert.Equal("Finland shelled the village of Mainila", ins.TextContent);

            var del = Assert.Single(mt.QuerySelectorAll("del.aot-diff__del"));
            Assert.Equal("the Soviet Union staged the Mainila shots", del.TextContent);
        });

        Assert.Equal(1, translator.TranslatePassageCalls);

        // Switching Lang fi -> en reuses the cached passage translation without calling translator again.
        cut.Render(p => p.Add(x => x.Passage, passage).Add(x => x.Lang, "fi"));
        Assert.Empty(cut.FindAll("[data-testid=diff-mt]"));

        cut.Render(p => p.Add(x => x.Passage, passage).Add(x => x.Lang, "en"));
        Assert.NotEmpty(cut.FindAll("[data-testid=diff-mt] ins.aot-diff__add"));
        Assert.Equal(1, translator.TranslatePassageCalls);
    }

    [Fact]
    public void Lang_en_unaligned_shows_plain_translation_and_note()
    {
        var translator = new FakeEvidenceTranslator()
            .ReturnsPassage(new AlignedTranslation(
                BeforeEn: "The Winter War began in November 1939.",
                AfterEn: "The Winter War began when Finland shelled Mainila in November 1939.",
                ChangedInAfterEn: [],
                ChangedInBeforeEn: [],
                Aligned: false,
                Ok: true));

        Services.AddSingleton<IEvidenceTranslator>(translator);

        var passage = MakePassage(
            "Talvisota alkoi marraskuussa 1939.",
            "Talvisota alkoi, kun Suomi ampui Mainilaan marraskuussa 1939.");

        var cut = Render<DiffViewer>(p => p
            .Add(x => x.Passage, passage)
            .Add(x => x.Lang, "en"));

        cut.WaitForAssertion(() =>
        {
            var mt = cut.Find("[data-testid=diff-mt]");
            Assert.Empty(mt.QuerySelectorAll("ins"));
            Assert.Empty(mt.QuerySelectorAll("del"));
            Assert.Contains("The Winter War began when Finland shelled Mainila in November 1939.", mt.TextContent);
            Assert.Equal(
                "Highlighting couldn't be aligned in the translation; showing it unaligned.",
                cut.Find("[data-testid=diff-mt-note]").TextContent);
        });
    }

    [Fact]
    public void Lang_en_translator_failure_shows_unavailable_note_and_keeps_FI_diff_intact()
    {
        var translator = new FakeEvidenceTranslator { FailAll = true };
        Services.AddSingleton<IEvidenceTranslator>(translator);

        var passage = MakePassage(
            "Talvisota alkoi 30. marraskuuta 1939.",
            "Talvisota alkoi 30. marraskuuta 1939. Suomi aloitti sodan.");

        var cut = Render<DiffViewer>(p => p
            .Add(x => x.Passage, passage)
            .Add(x => x.Lang, "en"));

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Translation unavailable.", cut.Find("[data-testid=diff-mt-note]").TextContent);
        });

        // FI diff above remains completely intact.
        var fiBody = cut.Find("[data-testid=diff-body-fi]");
        Assert.Contains("Talvisota alkoi 30. marraskuuta 1939.", fiBody.TextContent);
        var fiIns = Assert.Single(fiBody.QuerySelectorAll("ins.aot-diff__add"));
        Assert.Contains("Suomi aloitti sodan.", fiIns.TextContent);
    }

    [Fact]
    public async Task Lang_en_while_pending_folds_context_blocks_and_shows_Translating_status_before_resolving()
    {
        var pending = new PendingEvidenceTranslator();
        Services.AddSingleton<IEvidenceTranslator>(pending);

        const string before = """
            Ensimmäinen koskematon kappale kertoo taustasta.

            Toinen taustalause. Kolmas taustalause ennen muokkausta.

            Neljäs koskematon kappale sodan jälkeen.
            """;

        const string after = """
            Ensimmäinen koskematon kappale kertoo taustasta.

            Toinen taustalause. Kolmas taustalause ennen muokkausta. Suomi aloitti talvisodan.

            Neljäs koskematon kappale sodan jälkeen.
            """;

        var passage = MakePassage(before, after);
        var cut = Render<DiffViewer>(p => p
            .Add(x => x.Passage, passage)
            .Add(x => x.Lang, "en")
            .Add(x => x.Scope, "winter-war"));

        // While translation is in flight: clear "Translating…" status, context folded, only the changed sentence visible.
        var loading = cut.Find("[data-testid=diff-mt-loading]");
        Assert.Equal("Translating…", loading.TextContent.Trim());
        Assert.Equal("true", loading.GetAttribute("aria-busy"));

        var fiBodyWhileLoading = cut.Find("[data-testid=diff-body-fi]");
        Assert.Contains("Suomi aloitti talvisodan.", fiBodyWhileLoading.TextContent);
        Assert.DoesNotContain("Ensimmäinen koskematon kappale", fiBodyWhileLoading.TextContent);
        Assert.DoesNotContain("Toinen taustalause.", fiBodyWhileLoading.TextContent);
        Assert.DoesNotContain("Kolmas taustalause ennen muokkausta.", fiBodyWhileLoading.TextContent);
        Assert.DoesNotContain("Neljäs koskematon kappale", fiBodyWhileLoading.TextContent);
        Assert.Equal(2, cut.FindAll("[data-testid=diff-fold]").Count);

        pending.Tcs.SetResult(new AlignedTranslation(
            BeforeEn: "Third background sentence before edit.",
            AfterEn: "Third background sentence before edit. Finland started the Winter War.",
            ChangedInAfterEn: [(39, 31)],
            ChangedInBeforeEn: [],
            Aligned: true,
            Ok: true));

        await cut.InvokeAsync(() => Task.CompletedTask);
        cut.WaitForAssertion(() =>
        {
            Assert.Empty(cut.FindAll("[data-testid=diff-mt-loading]"));
            var ins = Assert.Single(cut.FindAll("[data-testid=diff-mt] ins.aot-diff__add"));
            Assert.Equal("Finland started the Winter War.", ins.TextContent);
            // Once translation arrives, 1 sentence of context around the change becomes visible.
            Assert.Contains("Kolmas taustalause ennen muokkausta.", cut.Find("[data-testid=diff-body-fi]").TextContent);
        });
    }

    private sealed class PendingEvidenceTranslator : IEvidenceTranslator
    {
        public TaskCompletionSource<AlignedTranslation> Tcs { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<TranslationResult> TranslateAsync(string text, string sourceLang = "fi", string? scope = null, CancellationToken ct = default) =>
            Task.FromResult(new TranslationResult(text, $"[EN] {text}", Ok: true));

        public Task<IReadOnlyList<TranslationResult>> TranslateManyAsync(IReadOnlyList<string> texts, string sourceLang = "fi", string? scope = null, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<TranslationResult>>(texts.Select(t => new TranslationResult(t, $"[EN] {t}", Ok: true)).ToArray());

        public Task<AlignedTranslation> TranslatePassageAsync(
            string before,
            string after,
            IReadOnlyList<(int Start, int Length)> changedInAfter,
            IReadOnlyList<(int Start, int Length)> changedInBefore,
            string sourceLang = "fi",
            string? scope = null,
            CancellationToken ct = default) => Tcs.Task;
    }
}
