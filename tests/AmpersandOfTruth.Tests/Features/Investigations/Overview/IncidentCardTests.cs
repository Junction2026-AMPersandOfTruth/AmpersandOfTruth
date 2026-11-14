using AmpersandOfTruth.Web.Components.Shared;
using AmpersandOfTruth.Web.Features.Evidence;
using AmpersandOfTruth.Web.Features.Investigations;
using AmpersandOfTruth.Web.Features.Investigations.View;
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
        Assert.Contains("Talvisota (Winter War)", card.TextContent);

        var claim = cut.Find("[data-testid=incident-claim]");
        Assert.Contains("Finland started the Winter War by shelling Mainila.", claim.TextContent);

        var original = cut.Find("[data-testid=incident-original]");
        Assert.Equal("fi", original.GetAttribute("lang"));
        Assert.Equal("Suomi aloitti talvisodan ampumalla Mainilan kylään.", original.TextContent);

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
        Assert.Empty(cutDel.FindAll("[data-testid=incident-original]"));
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
