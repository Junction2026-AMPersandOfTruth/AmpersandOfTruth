using AmpersandOfTruth.Web.Features.Investigations.Incident;
using AmpersandOfTruth.Web.Features.Investigations.View;
using AmpersandOfTruth.Web.Features.WikiDiff;

namespace AmpersandOfTruth.Tests.Features.Investigations.Incident;

public sealed class DiffViewerModelTests
{
    private static readonly string ParagraphBreak = new([(char)10, (char)10]);

    private static DiffPassage MakePassage(string before, string after, long revId = 101, long? parentRevId = 100) =>
        new(revId, parentRevId, before, after, WikitextDiff.Compare(before, after));

    [Fact]
    public void Insertion_in_middle_paragraph_produces_one_changed_block_with_Added_tokens_and_context_around_it()
    {
        const string before = """
            Ensimmäinen kappale kertoo taustasta.

            Toinen kappale kuvaa sodan alkua marraskuussa 1939.

            Kolmas kappale käsittelee rauhanneuvotteluja.
            """;

        const string after = """
            Ensimmäinen kappale kertoo taustasta.

            Toinen kappale kuvaa sodan alkua marraskuussa 1939. Suomi hyökkäsi Mainilaan.

            Kolmas kappale käsittelee rauhanneuvotteluja.
            """;

        var model = DiffModel.Build(MakePassage(before, after), DiffTextMode.Prose);

        Assert.Equal(3, model.Blocks.Count);
        Assert.False(model.Blocks[0].IsChanged);
        Assert.True(model.Blocks[1].IsChanged);
        Assert.False(model.Blocks[2].IsChanged);

        var changed = model.Blocks[1];
        Assert.Equal((1, 1), (changed.OldParagraph, changed.NewParagraph));
        Assert.Contains(changed.Tokens, t => t.Kind == DiffTokenKind.Added && t.Text.Contains("Suomi hyökkäsi Mainilaan."));
        Assert.DoesNotContain(changed.Tokens, t => t.Kind == DiffTokenKind.Removed);
        Assert.Equal("Toinen kappale kuvaa sodan alkua marraskuussa 1939.", changed.BeforeText);
        Assert.Equal("Toinen kappale kuvaa sodan alkua marraskuussa 1939. Suomi hyökkäsi Mainilaan.", changed.AfterText);

        // Prose mode keeps at most 1 sentence of context on each side: block 1 already has 1 sentence before the addition,
        // so block 0 folds while single-sentence block 2 provides the 1 sentence of right context.
        Assert.Collection(
            model.Items,
            item0 =>
            {
                var fold = Assert.IsType<DiffFoldItem>(item0);
                Assert.Equal(1, fold.Count);
                Assert.Equal("⋯ 1 unchanged line", fold.Label);
            },
            item1 => Assert.True(Assert.IsType<DiffBlockItem>(item1).Block.IsChanged),
            item2 => Assert.False(Assert.IsType<DiffBlockItem>(item2).Block.IsChanged));

        // Wikitext mode still keeps 1 surrounding block on each side.
        var wikitextModel = DiffModel.Build(MakePassage(before, after), DiffTextMode.Wikitext);
        Assert.All(wikitextModel.Items, item => Assert.IsType<DiffBlockItem>(item));
    }

    [Fact]
    public void Deletion_inside_and_of_whole_middle_paragraph_produces_Removed_tokens_in_place()
    {
        const string before = """
            Ensimmäinen kappale pysyy ennallaan.

            Tämä kappale poistetaan kokonaan artikkelista.

            Kolmas kappale pysyy myös ennallaan.
            """;

        const string after = """
            Ensimmäinen kappale pysyy ennallaan.

            Kolmas kappale pysyy myös ennallaan.
            """;

        var wholeDeleted = DiffModel.Build(MakePassage(before, after), DiffTextMode.Prose);

        Assert.Equal(3, wholeDeleted.Blocks.Count);
        Assert.False(wholeDeleted.Blocks[0].IsChanged);
        Assert.True(wholeDeleted.Blocks[1].IsChanged);
        Assert.False(wholeDeleted.Blocks[2].IsChanged);

        var removedBlock = wholeDeleted.Blocks[1];
        Assert.Equal((1, (int?)null), (removedBlock.OldParagraph, removedBlock.NewParagraph));
        var token = Assert.Single(removedBlock.Tokens);
        Assert.Equal(DiffTokenKind.Removed, token.Kind);
        Assert.Equal("Tämä kappale poistetaan kokonaan artikkelista.", token.Text);

        // Partial sentence deletion inside a paragraph keeps surrounding Same tokens + Removed token.
        var inlineDeleted = DiffModel.Build(
            MakePassage(
                "Alku. Neuvostoliitto lavasti laukaukset. Loppu.",
                "Alku. Loppu."),
            DiffTextMode.Prose);

        var single = Assert.Single(inlineDeleted.Blocks);
        Assert.True(single.IsChanged);
        Assert.Contains(single.Tokens, t => t.Kind == DiffTokenKind.Removed && t.Text.Contains("Neuvostoliitto lavasti laukaukset."));
        Assert.Contains(single.Tokens, t => t.Kind == DiffTokenKind.Same);
    }

    [Fact]
    public void Folding_collapses_unchanged_blocks_beyond_one_context_block_with_exact_counts()
    {
        var oldParas = Enumerable.Range(0, 9).Select(i => $"Kappale {i} tekstiä.").ToArray();
        var newParas = oldParas.ToArray();
        newParas[5] = "Kappale 5 tekstiä. Lisätty väite tähän.";

        var passage = MakePassage(string.Join(ParagraphBreak, oldParas), string.Join(ParagraphBreak, newParas));
        var proseModel = DiffModel.Build(passage, DiffTextMode.Prose);
        var wikiModel = DiffModel.Build(passage, DiffTextMode.Wikitext);

        Assert.Equal(9, proseModel.Blocks.Count);

        // Prose mode: block 5 already starts with 1 unchanged sentence ("Kappale 5 tekstiä."), so blocks 0..4 all fold (5),
        // while single-sentence block 6 provides the 1 sentence after the addition and blocks 7..8 fold (2).
        Assert.Collection(
            proseModel.Items,
            item0 =>
            {
                var fold = Assert.IsType<DiffFoldItem>(item0);
                Assert.Equal(5, fold.Count);
                Assert.Equal("⋯ 5 unchanged lines", fold.Label);
            },
            item1 => Assert.True(Assert.IsType<DiffBlockItem>(item1).Block.IsChanged),
            item2 => Assert.False(Assert.IsType<DiffBlockItem>(item2).Block.IsChanged),
            item3 =>
            {
                var fold = Assert.IsType<DiffFoldItem>(item3);
                Assert.Equal(2, fold.Count);
                Assert.Equal("⋯ 2 unchanged lines", fold.Label);
            });

        // Wikitext mode keeps 1 block of context on each side: Fold(0..3 = 4), Block(4), Block(5), Block(6), Fold(7..8 = 2).
        Assert.Collection(
            wikiModel.Items,
            item0 =>
            {
                var fold = Assert.IsType<DiffFoldItem>(item0);
                Assert.Equal(4, fold.Count);
                Assert.Equal("⋯ 4 unchanged lines", fold.Label);
            },
            item1 => Assert.False(Assert.IsType<DiffBlockItem>(item1).Block.IsChanged),
            item2 => Assert.True(Assert.IsType<DiffBlockItem>(item2).Block.IsChanged),
            item3 => Assert.False(Assert.IsType<DiffBlockItem>(item3).Block.IsChanged),
            item4 =>
            {
                var fold = Assert.IsType<DiffFoldItem>(item4);
                Assert.Equal(2, fold.Count);
                Assert.Equal("⋯ 2 unchanged lines", fold.Label);
            });
    }

    [Fact]
    public void Prose_mode_folds_sentences_inside_long_changed_paragraph_and_folds_untouched_neighbour_paragraphs()
    {
        const string before = """
            Johdannon ensimmäinen lause. Johdannon toinen lause.

            Ensimmäinen taustalause. Toinen taustalause 30. marraskuuta 1939. C. G. E. Mannerheim johti joukkoja. Neljäs lause sodan jälkeen. Viides lause lopussa.

            Punaisen ja valkoisen terrorin keskeisiä toteuttajia tutkittiin pitkään. Toinen pitkä lause tässä kappaleessa. Kolmas pitkä lause samassa kappaleessa.
            """;

        const string after = """
            Johdannon ensimmäinen lause. Johdannon toinen lause.

            Ensimmäinen taustalause. Toinen taustalause 30. marraskuuta 1939. C. G. E. Mannerheim johti joukkoja. Leninin kirja sisälsi suoran kehotuksen. Neljäs lause sodan jälkeen. Viides lause lopussa.

            Punaisen ja valkoisen terrorin keskeisiä toteuttajia tutkittiin pitkään. Toinen pitkä lause tässä kappaleessa. Kolmas pitkä lause samassa kappaleessa.
            """;

        var model = DiffModel.Build(MakePassage(before, after), DiffTextMode.Prose);

        // Items: Fold(para0 + 2 earlier sentences = 3), Block(1 sentence before + Added + 1 sentence after), Fold(1 trailing sentence + para2 = 2).
        Assert.Collection(
            model.Items,
            item0 =>
            {
                var fold = Assert.IsType<DiffFoldItem>(item0);
                Assert.Equal(3, fold.Count);
                Assert.Equal("⋯ 3 unchanged lines", fold.Label);
            },
            item1 =>
            {
                var block = Assert.IsType<DiffBlockItem>(item1).Block;
                Assert.True(block.IsChanged);
                Assert.Contains("C. G. E. Mannerheim johti joukkoja.", block.AfterText);
                Assert.Contains("Leninin kirja sisälsi suoran kehotuksen.", block.AfterText);
                Assert.Contains("Neljäs lause sodan jälkeen.", block.AfterText);
                Assert.DoesNotContain("Ensimmäinen taustalause.", block.AfterText);
                Assert.DoesNotContain("Toinen taustalause", block.AfterText);
                Assert.DoesNotContain("Viides lause lopussa.", block.AfterText);
            },
            item2 =>
            {
                var fold = Assert.IsType<DiffFoldItem>(item2);
                Assert.Equal(2, fold.Count);
                Assert.Equal("⋯ 2 unchanged lines", fold.Label);
                Assert.DoesNotContain(
                    model.Items.OfType<DiffBlockItem>(),
                    b => b.Block.AfterText.Contains("Punaisen ja valkoisen terrorin"));
            });

        // ChangedOnlyItems (used while EN is loading) folds even the 1-sentence context so only the changed span shows.
        var changedOnlyBlock = Assert.Single(model.ChangedOnlyItems.OfType<DiffBlockItem>()).Block;
        Assert.Equal("Leninin kirja sisälsi suoran kehotuksen.", changedOnlyBlock.AfterText);
    }

    [Fact]
    public void CleanProse_decodes_html_entities_and_strips_wiki_and_infobox_markup_while_preserving_literal_script_tags()
    {
        Assert.Equal("b", DiffModel.CleanProse("[[a|b]]"));
        Assert.Equal("a", DiffModel.CleanProse("[[a]]"));
        Assert.Equal(
            "Leninin Helsingissä kirjoittama kirja \"Valtio ja vallankumous\"",
            DiffModel.CleanProse("Leninin Helsingissä kirjoittama kirja \"[[Valtio ja vallankumous]]\""));
        Assert.Equal(
            "Lihavoitu ja kursivoitu teksti",
            DiffModel.CleanProse("'''Lihavoitu''' ja ''kursivoitu'' teksti"));
        Assert.Equal(
            "1 000–1 500 & lähteet",
            DiffModel.CleanProse("| tappiot2 = 1&nbsp;000–1&nbsp;500 &amp; [[Lähde|lähteet]]"));
        Assert.Equal(
            "<script>alert('xss')</script>",
            DiffModel.CleanProse("<script>alert('xss')</script>"));
    }

    [Fact]
    public void Prose_mode_strips_wikitext_markup_and_surfaces_citation_changes_while_Wikitext_mode_keeps_raw_syntax()
    {
        const string before = "'''Talvisota''' alkoi [[Suomi|Suomessa]].<ref>Tanner 1950, s. 112</ref>";
        const string after = "'''Talvisota''' alkoi [[Suomi|Suomessa]] {{Lähde}} ja laajeni.";

        var passage = MakePassage(before, after);
        var prose = DiffModel.Build(passage, DiffTextMode.Prose);
        var wiki = DiffModel.Build(passage, DiffTextMode.Wikitext);

        var proseBlock = Assert.Single(prose.Blocks);
        Assert.DoesNotContain("'''", proseBlock.AfterText);
        Assert.DoesNotContain("[[", proseBlock.AfterText);
        Assert.DoesNotContain("{{Lähde}}", proseBlock.AfterText);
        Assert.Contains("[citation: Tanner 1950, s. 112]", proseBlock.BeforeText);
        Assert.Contains("ja laajeni", proseBlock.AfterText);

        var wikiBlock = Assert.Single(wiki.Blocks);
        Assert.Contains("'''Talvisota'''", wikiBlock.AfterText);
        Assert.Contains("[[Suomi|Suomessa]]", wikiBlock.AfterText);
        Assert.Contains("{{Lähde}}", wikiBlock.AfterText);
    }

    [Fact]
    public void Highlight_marks_verbatim_quotes_within_and_across_tokens_and_TranslationInput_maps_exact_offsets()
    {
        var passage = MakePassage(
            "Teloitettiin 8 400 punaista vankia.",
            "Teloitettiin 840 punaista vankia.");

        var model = DiffModel.Build(passage, DiffTextMode.Prose);
        var block = Assert.Single(model.Blocks);

        var highlighted = DiffModel.Highlight(block.Tokens, ["Teloitettiin 840 punaista"]);
        var markedText = string.Concat(highlighted.SelectMany(t => t.Spans).Where(s => s.Highlighted).Select(s => s.Text));
        Assert.Equal("Teloitettiin 840 punaista", markedText);

        var input = DiffModel.BuildTranslationInput(passage);
        Assert.False(input.IsEmpty);
        var beforeSpan = Assert.Single(input.ChangedInBefore);
        var afterSpan = Assert.Single(input.ChangedInAfter);
        Assert.Equal("8 400", input.Before.Substring(beforeSpan.Start, beforeSpan.Length));
        Assert.Equal("840", input.After.Substring(afterSpan.Start, afterSpan.Length));
    }
}
