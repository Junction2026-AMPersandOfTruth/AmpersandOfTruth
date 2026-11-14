using AmpersandOfTruth.Web.Components.Shared;
using AmpersandOfTruth.Web.Features.Evidence;
using AmpersandOfTruth.Web.Features.Investigations;
using AmpersandOfTruth.Web.Features.Investigations.View;
using AmpersandOfTruth.Web.Features.WikiDiff;
using Bunit;

namespace AmpersandOfTruth.Tests.Features.Investigations.Overview;

public sealed class IncidentCardTests : BunitContext
{
    [Fact]
    public void Renders_tier_article_claim_fi_original_what_happened_facts_and_max_four_chips_with_more_count()
    {
        var incident = MakeView(
            id: "inc-42",
            tier: EvidenceTier.Campaign,
            kind: IncidentKind.Insertion,
            claimEn: "Finland started the Winter War by shelling Mainila.",
            claimOriginal: "Suomi aloitti talvisodan ampumalla Mainilan kylään.",
            signals: ["reinserted", "citation_removed", "new_accounts", "multiple_articles", "persistence", "anonymous"]);

        var cut = Render<IncidentCard>(p => p
            .Add(x => x.Incident, incident)
            .Add(x => x.Topic, "winter-war")
            .Add(x => x.Query, "?sort=recent&tier=2,3"));

        var card = cut.Find("[data-testid=incident-card]");
        Assert.Equal("/investigate/winter-war/incidents/inc-42?sort=recent&tier=2,3", card.GetAttribute("href"));
        Assert.Equal("Winter War (Talvisota)", cut.Find("[data-testid=incident-article]").TextContent.Trim());

        var mt = Assert.Single(cut.FindAll("[data-testid=incident-claim-mt]"));
        Assert.Equal("Automated English summary", mt.TextContent.Trim());
        Assert.Equal(Attribution.MachineTranslationNotice, mt.QuerySelector(".aot-mt__tag")?.GetAttribute("title"));

        var claim = cut.Find("[data-testid=incident-claim]");
        Assert.Equal("Finland started the Winter War by shelling Mainila.", claim.TextContent);
        Assert.DoesNotContain("\u201C", claim.TextContent);

        var original = cut.Find("[data-testid=incident-original]");
        Assert.Equal("fi", original.GetAttribute("lang"));
        Assert.Equal("Suomi aloitti talvisodan ampumalla Mainilan kylään.", original.TextContent);
        Assert.Equal("incident-original", claim.NextElementSibling?.GetAttribute("data-testid"));

        var what = cut.Find("[data-testid=incident-what]");
        Assert.Equal("Inserted 4 times by 3 accounts over 2 years; live for 61 days in total.", what.TextContent);

        Assert.Contains("3 Mar 2014", card.TextContent);
        Assert.Contains("9 Mar 2016", card.TextContent);
        Assert.Contains("61 days", card.TextContent);

        var chips = cut.FindAll("[data-testid=signal-chip]");
        Assert.Equal(4, chips.Count);
        Assert.Equal("+2 more", cut.Find("[data-testid=incident-more-signals]").TextContent);
    }

    [Fact]
    public void Null_claim_uses_article_fallback_and_deletion_prefixes_removed()
    {
        var nullClaim = MakeView(
            id: "inc-null",
            tier: EvidenceTier.Reverted,
            kind: IncidentKind.Insertion,
            claimEn: null,
            claimOriginal: "Lisätty teksti.");

        var cutNull = Render<IncidentCard>(p => p
            .Add(x => x.Incident, nullClaim)
            .Add(x => x.Topic, "winter-war"));

        Assert.Equal("Text added to Winter War", cutNull.Find("[data-testid=incident-claim]").TextContent);
        Assert.Empty(cutNull.FindAll("[data-testid=incident-claim-mt]"));

        var deletion = MakeView(
            id: "inc-del",
            tier: EvidenceTier.Contested,
            kind: IncidentKind.Deletion,
            claimEn: "casualty figures from the opening paragraph",
            claimOriginal: null);

        var cutDel = Render<IncidentCard>(p => p
            .Add(x => x.Incident, deletion)
            .Add(x => x.Topic, "winter-war"));

        Assert.Equal("Removed: casualty figures from the opening paragraph", cutDel.Find("[data-testid=incident-claim]").TextContent);
        Assert.Single(cutDel.FindAll("[data-testid=incident-claim-mt]"));
        Assert.Equal("Removed 4 times by 3 accounts over 2 years; stayed out for 61 days in total.", cutDel.Find("[data-testid=incident-what]").TextContent);
        Assert.Empty(cutDel.FindAll("[data-testid=incident-original]"));

        var insertionWithRemovedPrefix = MakeView(
            id: "inc-rem-prefix",
            tier: EvidenceTier.Contested,
            kind: IncidentKind.Insertion,
            claimEn: "Removed: Finland's defeat from the summary box.",
            claimOriginal: null)
            with { Articles = [new ArticleRef(250972, "Suomen sisällissodan nimet", null)] };

        var cutRemPrefix = Render<IncidentCard>(p => p
            .Add(x => x.Incident, insertionWithRemovedPrefix)
            .Add(x => x.Topic, "civil-war-1918"));

        Assert.Equal("Removed: Finland's defeat from the summary box.", cutRemPrefix.Find("[data-testid=incident-claim]").TextContent);
        Assert.Contains("Suomen sisällissodan nimet (Finnish Wikipedia)", cutRemPrefix.Find("[data-testid=incident-card]").TextContent);
    }

    [Fact]
    public void Replacement_cards_with_added_text_use_changed_prefix_while_genuine_deletions_keep_removed()
    {
        // Real pipeline shape (Card #3 on civil-war-1918): removed prose slightly longer than added prose (< 20 chars),
        // so IncidentMapper derives Kind = Deletion even though the edit replaced text and ClaimEn states what was added.
        const string beforeOulu = "'''Oulun taistelu''' käytiin helmikuussa 1918 valkoisten ja punaisten välillä.";
        const string afterOulu = "'''Oulun valtaus''' käytiin 1918.";
        var replaceDiff = WikitextDiff.Compare(beforeOulu, afterOulu);
        var replacement = MakeView(
            id: "inc-replace",
            tier: EvidenceTier.Contested,
            kind: IncidentKind.Deletion,
            claimEn: "The 1918 engagement in Oulu should be titled 'Oulun valtaus' rather than 'Oulun taistelu'.",
            claimOriginal: "Vuoden 1918 yhteenotto Oulussa tulisi nimetä Oulun valtaukseksi.")
            with
            {
                Articles = [new ArticleRef(8810, "Oulun taistelu", "Battle of Oulu")],
                Passage = new DiffPassage(1001, 1000, beforeOulu, afterOulu, replaceDiff),
            };

        var cutRep = Render<IncidentCard>(p => p
            .Add(x => x.Incident, replacement)
            .Add(x => x.Topic, "civil-war-1918"));

        Assert.Equal(
            "Changed: The 1918 engagement in Oulu should be titled 'Oulun valtaus' rather than 'Oulun taistelu'.",
            cutRep.Find("[data-testid=incident-claim]").TextContent);
        Assert.Equal(
            "Changed 4 times by 3 accounts over 2 years; live for 61 days in total.",
            cutRep.Find("[data-testid=incident-what]").TextContent);
        Assert.Equal("Battle of Oulu (Oulun taistelu)", cutRep.Find("[data-testid=incident-article]").TextContent.Trim());
        Assert.Empty(CopyRules.FindViolations(cutRep.Find("[data-testid=incident-card]").TextContent));

        // Genuine pure deletion (AddedProseChars == 0) keeps "Removed: ..." and "Removed N times ...; stayed out for ..."
        const string beforePetsamo = "Neuvosto-Venäjä suostui vaihtamaan Petsamon alueen. Toinen lause jää.";
        const string afterPetsamo = "Toinen lause jää.";
        var pureDeleteDiff = WikitextDiff.Compare(beforePetsamo, afterPetsamo);
        var pureDeletion = MakeView(
            id: "inc-pure-del",
            tier: EvidenceTier.Contested,
            kind: IncidentKind.Deletion,
            claimEn: "Soviet Russia agreed to exchange Petsamo.",
            claimOriginal: "Neuvosto-Venäjä suostui vaihtamaan Petsamon alueen.")
            with
            {
                Passage = new DiffPassage(1001, 1000, beforePetsamo, afterPetsamo, pureDeleteDiff),
            };

        var cutPureDel = Render<IncidentCard>(p => p
            .Add(x => x.Incident, pureDeletion)
            .Add(x => x.Topic, "civil-war-1918"));

        Assert.Equal("Removed: Soviet Russia agreed to exchange Petsamo.", cutPureDel.Find("[data-testid=incident-claim]").TextContent);
        Assert.Equal("Removed 4 times by 3 accounts over 2 years; stayed out for 61 days in total.", cutPureDel.Find("[data-testid=incident-what]").TextContent);

        // Article header when English and Finnish titles match (e.g. "Mannerheim") does not repeat "(Mannerheim)"
        var sameTitle = MakeView("inc-same", EvidenceTier.Reverted, IncidentKind.Insertion, "Claim.", null)
            with { Articles = [new ArticleRef(999, "Mannerheim", "Mannerheim")] };
        var cutSame = Render<IncidentCard>(p => p.Add(x => x.Incident, sameTitle).Add(x => x.Topic, "mannerheim"));
        Assert.Equal("Mannerheim", cutSame.Find("[data-testid=incident-article]").TextContent.Trim());
    }

    [Fact]
    public void Highlight_and_hover_callbacks_work_and_hostile_strings_are_html_encoded()
    {
        const string payload = "<script>alert(1)</script>";
        string? hovered = "initial";

        var incident = MakeView(
            id: "inc-xss",
            tier: EvidenceTier.Contested,
            kind: IncidentKind.Insertion,
            claimEn: payload,
            claimOriginal: payload);

        var cut = Render<IncidentCard>(p => p
            .Add(x => x.Incident, incident)
            .Add(x => x.Topic, "winter-war")
            .Add(x => x.Highlighted, true)
            .Add(x => x.OnHover, id => hovered = id));

        var card = cut.Find("[data-testid=incident-card]");
        Assert.Contains("is-highlighted", card.ClassList);
        Assert.Empty(cut.FindAll("script"));
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", cut.Markup);

        card.MouseEnter();
        Assert.Equal("inc-xss", hovered);

        card.MouseLeave();
        Assert.Null(hovered);
    }

    [Fact]
    public void Renders_wikipedia_diff_link_for_first_inserting_revision_outside_main_link_without_nested_anchors()
    {
        var incident = MakeView(
            id: "inc-wiki",
            tier: EvidenceTier.Contested,
            kind: IncidentKind.Insertion,
            claimEn: "Disputed statement.",
            claimOriginal: "Kiistanalainen väite.");

        var cut = Render<IncidentCard>(p => p
            .Add(x => x.Incident, incident)
            .Add(x => x.Topic, "winter-war"));

        Assert.Empty(cut.FindAll("a a"));

        var mainLink = cut.Find("[data-testid=incident-card]");
        Assert.Null(mainLink.QuerySelector("[data-testid=incident-wiki-diff]"));

        var wikiLink = cut.Find("[data-testid=incident-wiki-diff]");
        Assert.Equal("View on Wikipedia \u2197", wikiLink.TextContent.Trim());
        Assert.Equal(WikipediaLinks.Diff(1001).AbsoluteUri, wikiLink.GetAttribute("href"));
        Assert.Equal("_blank", wikiLink.GetAttribute("target"));
        Assert.Equal("noopener noreferrer", wikiLink.GetAttribute("rel"));
        Assert.Empty(CopyRules.FindViolations(wikiLink.TextContent, includeJargon: true));
    }

    [Fact]
    public void Compact_mode_adds_compact_class_and_omits_facts_and_wikipedia_diff_link()
    {
        var incident = MakeView(
            id: "inc-compact",
            tier: EvidenceTier.Campaign,
            kind: IncidentKind.Insertion,
            claimEn: "Compact claim.",
            claimOriginal: "Tiivis väite.",
            signals: ["removed_by_others", "multiple_editors"]);

        var cut = Render<IncidentCard>(p => p
            .Add(x => x.Incident, incident)
            .Add(x => x.Topic, "winter-war")
            .Add(x => x.Compact, true));

        var articleEl = cut.Find("article.aot-card");
        Assert.Contains("aot-card--compact", articleEl.ClassList);
        Assert.Equal("/investigate/winter-war/incidents/inc-compact", cut.Find("[data-testid=incident-card]").GetAttribute("href"));
        Assert.Empty(cut.FindAll("[data-testid=incident-facts]"));
        Assert.Empty(cut.FindAll("[data-testid=incident-wiki-diff]"));
        Assert.Empty(cut.FindAll("[data-testid=signal-chip]"));
    }

    private static IncidentView MakeView(
        string id,
        EvidenceTier tier,
        IncidentKind kind,
        string? claimEn,
        string? claimOriginal,
        IReadOnlyList<string>? signals = null)
    {
        var firstSeen = new DateTimeOffset(2014, 3, 3, 14, 2, 0, TimeSpan.Zero);
        var lastRemoved = new DateTimeOffset(2016, 3, 9, 9, 0, 0, TimeSpan.Zero);

        var sigs = (signals ?? ["removed_by_others"])
            .Select(s => new SignalView(SignalCatalog.For(s), null))
            .ToList();

        var edits = new List<EditView>
        {
            new(1001, 1000, 1150, firstSeen, EditorDisplay.For("UserA"), null, EditAction.Inserted, TimeSpan.FromDays(30)),
            new(1002, 1001, 1150, lastRemoved, EditorDisplay.For("UserB"), null, EditAction.Reinserted, TimeSpan.FromDays(31)),
        };

        var source = new IncidentRecord
        {
            Id = id,
            PageId = 1150,
            InsertingRevIds = [1001, 1002],
            RemovingRevIds = [1003],
            Tier = tier,
            Score = 50.0,
        };

        return new IncidentView(
            Id: id,
            Rank: 1,
            Tier: tier,
            TierReason: EvidenceTiers.Get(tier).Label,
            ClaimEn: claimEn,
            ClaimOriginal: claimOriginal,
            Lang: "fi",
            Kind: kind,
            Articles: [new ArticleRef(1150, "Talvisota", "Winter War"), new ArticleRef(1200, "Mainilan laukaukset", "Shelling of Mainila")],
            FirstSeen: firstSeen,
            LastRemoved: lastRemoved,
            TotalLive: TimeSpan.FromDays(61),
            AccountCount: 3,
            InsertCount: 4,
            Signals: sigs,
            Edits: edits,
            Passage: null,
            Assessment: null,
            Relevance: 50.0,
            ClusterId: null,
            Source: source);
    }
}
