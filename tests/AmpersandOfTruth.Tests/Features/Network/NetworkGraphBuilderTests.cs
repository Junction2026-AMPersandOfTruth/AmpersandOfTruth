using AmpersandOfTruth.Tests.Features.Investigations;
using AmpersandOfTruth.Tests.Features.Investigations.View;
using AmpersandOfTruth.Web.Features.Evidence;
using AmpersandOfTruth.Web.Features.Investigations;
using AmpersandOfTruth.Web.Features.Investigations.Fixtures;
using AmpersandOfTruth.Web.Features.Investigations.View;
using AmpersandOfTruth.Web.Features.Network;
using Microsoft.Extensions.Logging.Abstractions;

namespace AmpersandOfTruth.Tests.Features.Network;

public sealed class NetworkGraphBuilderTests
{
    [Fact]
    public async Task Sample_incidents_winter_war_builds_nodes_role_counts_and_is_not_too_small()
    {
        var samples = new SampleIncidents(NullLogger<SampleIncidents>.Instance);
        var topic = samples.Match("winter-war");
        Assert.NotNull(topic);

        var mapper = new IncidentMapper(new FakeIncidentCorpus(), new FakeRevisionStore());
        var views = new List<IncidentView>();
        for (var i = 0; i < topic.Incidents.Count; i++)
        {
            views.Add(await mapper.MapAsync(topic.Incidents[i], i + 1));
        }

        var graph = NetworkGraphBuilder.Build(views, NetworkFilter.Default);

        Assert.False(graph.TooSmall);
        Assert.True(graph.Accounts.Count >= 4);
        Assert.NotEmpty(graph.Articles);
        Assert.NotEmpty(graph.Cells);
        Assert.True(graph.HasAnonymousAccounts);

        // In ww-01 on page 1256 (Talvisota), 85.76.x.x inserted once and re-inserted 3 times
        var anonAccount = Assert.Single(graph.Accounts, a => a.Key == "ip4:85.76");
        Assert.True(anonAccount.Anonymous);

        var anonCell = Assert.Single(graph.Cells, c => c.AccountKey == "ip4:85.76" && c.PageId == 1256);
        Assert.Equal(1, anonCell.Inserted);
        Assert.Equal(3, anonCell.Reinserted);
        Assert.Equal(0, anonCell.Removed);
        Assert.Equal(4, anonCell.Edits.Count);
        Assert.All(anonCell.Edits, e => Assert.Equal("ww-01", e.IncidentId));
    }

    [Fact]
    public void Aggregates_counts_per_role_preserves_edit_order_and_collects_cluster_ids()
    {
        var pageA = new ArticleRef(100, "Talvisota", "Winter War");
        var inc1 = MakeIncident(
            id: "inc-1",
            tier: EvidenceTier.Campaign,
            articles: [pageA],
            clusterId: "cluster-alpha",
            edits:
            [
                MakeEdit(101, 100, 2014, "EditorOne", EditAction.Inserted),
                MakeEdit(102, 100, 2014, "ReviewerA", EditAction.Removed),
                MakeEdit(103, 100, 2014, "EditorOne", EditAction.Reinserted),
            ]);

        var inc2 = MakeIncident(
            id: "inc-2",
            tier: EvidenceTier.Contested,
            articles: [pageA],
            clusterId: null,
            sourceClusterId: "cluster-beta",
            edits:
            [
                MakeEdit(201, 100, 2015, "EditorOne", EditAction.Reinserted),
                MakeEdit(202, 100, 2015, "EditorOne", EditAction.Removed),
            ]);

        var graph = NetworkGraphBuilder.Build([inc1, inc2], NetworkFilter.Default);

        var cell = Assert.Single(graph.Cells, c => c.AccountKey == "user:EditorOne" && c.PageId == 100);
        Assert.Equal(1, cell.Inserted);
        Assert.Equal(1, cell.Removed);
        Assert.Equal(2, cell.Reinserted);
        Assert.Equal(
            [("inc-1", 101L), ("inc-1", 103L), ("inc-2", 201L), ("inc-2", 202L)],
            cell.Edits);
        Assert.Equal(["cluster-alpha", "cluster-beta"], cell.ClusterIds);
    }

    [Fact]
    public void Orders_accounts_by_articles_touched_desc_then_edits_desc_and_articles_by_accounts_desc()
    {
        var art1 = new ArticleRef(10, "Artikkeli 10", null);
        var art2 = new ArticleRef(20, "Artikkeli 20", null);
        var art3 = new ArticleRef(30, "Artikkeli 30", null);

        // MultiThree touches 3 articles (3 edits total)
        // MultiTwoHeavy touches 2 articles (4 edits total)
        // MultiTwoLight touches 2 articles (2 edits total)
        // SingleHeavy touches 1 article (5 edits total)
        var incident = MakeIncident(
            id: "inc-order",
            tier: EvidenceTier.Contested,
            articles: [art1, art2, art3],
            edits:
            [
                MakeEdit(1, 10, 2018, "MultiThree", EditAction.Inserted),
                MakeEdit(2, 20, 2018, "MultiThree", EditAction.Inserted),
                MakeEdit(3, 30, 2018, "MultiThree", EditAction.Inserted),

                MakeEdit(4, 10, 2018, "MultiTwoHeavy", EditAction.Inserted),
                MakeEdit(5, 10, 2018, "MultiTwoHeavy", EditAction.Reinserted),
                MakeEdit(6, 20, 2018, "MultiTwoHeavy", EditAction.Inserted),
                MakeEdit(7, 20, 2018, "MultiTwoHeavy", EditAction.Reinserted),

                MakeEdit(8, 10, 2018, "MultiTwoLight", EditAction.Inserted),
                MakeEdit(9, 20, 2018, "MultiTwoLight", EditAction.Inserted),

                MakeEdit(10, 10, 2018, "SingleHeavy", EditAction.Inserted),
                MakeEdit(11, 10, 2018, "SingleHeavy", EditAction.Reinserted),
                MakeEdit(12, 10, 2018, "SingleHeavy", EditAction.Reinserted),
                MakeEdit(13, 10, 2018, "SingleHeavy", EditAction.Reinserted),
                MakeEdit(14, 10, 2018, "SingleHeavy", EditAction.Removed),
            ]);

        var graph = NetworkGraphBuilder.Build([incident], NetworkFilter.Default);

        Assert.Equal(
            ["user:MultiThree", "user:MultiTwoHeavy", "user:MultiTwoLight", "user:SingleHeavy"],
            graph.Accounts.Select(a => a.Key));

        // Page 10 has 4 accounts, Page 20 has 3 accounts, Page 30 has 1 account
        Assert.Equal([10L, 20L, 30L], graph.Articles.Select(a => a.PageId));
    }

    [Fact]
    public void Caps_graph_at_40_accounts_and_25_articles_and_reports_omitted_counts()
    {
        var articles = Enumerable.Range(1, 30)
            .Select(i => new ArticleRef(i, $"Artikkeli {i}", null))
            .ToList();

        var edits = new List<EditView>();
        long revId = 1000;
        for (var accountIdx = 1; accountIdx <= 45; accountIdx++)
        {
            var pageId = ((accountIdx - 1) % 30) + 1;
            edits.Add(MakeEdit(revId++, pageId, 2020, $"Account_{accountIdx:D2}", EditAction.Inserted));
        }

        var incident = MakeIncident("inc-cap", EvidenceTier.Contested, articles, edits);
        var graph = NetworkGraphBuilder.Build([incident], NetworkFilter.Default);

        Assert.Equal(40, graph.Accounts.Count);
        Assert.Equal(25, graph.Articles.Count);
        Assert.Equal(5, graph.OmittedAccounts);
        Assert.Equal(5, graph.OmittedArticles);

        var keptAccounts = graph.Accounts.Select(a => a.Key).ToHashSet(StringComparer.Ordinal);
        var keptArticles = graph.Articles.Select(a => a.PageId).ToHashSet();
        Assert.All(graph.Cells, cell =>
        {
            Assert.Contains(cell.AccountKey, keptAccounts);
            Assert.Contains(cell.PageId, keptArticles);
        });
    }

    [Fact]
    public void Groups_anonymous_editors_by_masked_prefix_grouping_key()
    {
        var article = new ArticleRef(500, "Jatkosota", "Continuation War");
        var incident = MakeIncident(
            id: "inc-anon",
            tier: EvidenceTier.Contested,
            articles: [article],
            edits:
            [
                MakeEdit(1, 500, 2016, "85.76.12.34", EditAction.Inserted),
                MakeEdit(2, 500, 2016, "85.76.200.99", EditAction.Reinserted),
                MakeEdit(3, 500, 2016, "2001:14ba:1:2::3", EditAction.Inserted),
                MakeEdit(4, 500, 2016, "2001:14ba:9:8::7", EditAction.Reinserted),
                MakeEdit(5, 500, 2016, "NamedReviewer", EditAction.Removed),
            ]);

        var graph = NetworkGraphBuilder.Build([incident], NetworkFilter.Default);

        Assert.Equal(3, graph.Accounts.Count);
        Assert.True(graph.TooSmall);

        var ipv4Node = Assert.Single(graph.Accounts, a => a.Key == "ip4:85.76");
        Assert.True(ipv4Node.Anonymous);
        Assert.Equal("85.76.x.x", ipv4Node.Label.DisplayName);

        var ipv6Node = Assert.Single(graph.Accounts, a => a.Key == "ip6:2001:14BA");
        Assert.True(ipv6Node.Anonymous);

        var ipv4Cell = Assert.Single(graph.Cells, c => c.AccountKey == "ip4:85.76");
        Assert.Equal(1, ipv4Cell.Inserted);
        Assert.Equal(1, ipv4Cell.Reinserted);
    }

    [Fact]
    public void Removers_toggle_tier_filter_and_year_range_filter_work_as_expected()
    {
        var art1 = new ArticleRef(1, "Talvisota", "Winter War");
        var art2 = new ArticleRef(2, "Mainilan laukaukset", "Shelling of Mainila");

        var lowTier2012 = MakeIncident(
            id: "inc-2012",
            tier: EvidenceTier.Reverted,
            articles: [art1],
            edits:
            [
                MakeEdit(10, 1, 2012, "EarlyInserter", EditAction.Inserted),
                MakeEdit(11, 1, 2012, "EarlyRemover", EditAction.Removed),
            ]);

        var campaign2019 = MakeIncident(
            id: "inc-2019",
            tier: EvidenceTier.Campaign,
            articles: [art2],
            edits:
            [
                MakeEdit(20, 2, 2019, "CampaignInserter", EditAction.Inserted),
                MakeEdit(21, 2, 2019, "CampaignInserter", EditAction.Reinserted),
                MakeEdit(22, 2, 2019, "PureRemover", EditAction.Removed),
            ]);

        // Exclude removers
        var withoutRemovers = NetworkGraphBuilder.Build(
            [lowTier2012, campaign2019],
            new NetworkFilter(MinTier: null, FromYear: null, ToYear: null, IncludeRemovers: false));
        Assert.DoesNotContain(withoutRemovers.Accounts, a => a.Key is "user:EarlyRemover" or "user:PureRemover");
        Assert.All(withoutRemovers.Cells, c => Assert.Equal(0, c.Removed));

        // MinTier = Campaign drops Reverted incident
        var campaignOnly = NetworkGraphBuilder.Build(
            [lowTier2012, campaign2019],
            new NetworkFilter(MinTier: EvidenceTier.Campaign, FromYear: null, ToYear: null, IncludeRemovers: true));
        Assert.Equal(["user:CampaignInserter", "user:PureRemover"], campaignOnly.Accounts.Select(a => a.Key));
        Assert.Single(campaignOnly.Articles, a => a.PageId == 2);

        // Year range (including swapped FromYear > ToYear) keeps only 2012
        var yearFiltered = NetworkGraphBuilder.Build(
            [lowTier2012, campaign2019],
            new NetworkFilter(MinTier: null, FromYear: 2015, ToYear: 2010, IncludeRemovers: true));
        Assert.Equal(["user:EarlyInserter", "user:EarlyRemover"], yearFiltered.Accounts.Select(a => a.Key));
        Assert.Single(yearFiltered.Articles, a => a.PageId == 1);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(3, true)]
    [InlineData(4, false)]
    [InlineData(6, false)]
    public void TooSmall_is_true_when_fewer_than_four_accounts(int accountCount, bool expectedTooSmall)
    {
        var article = new ArticleRef(77, "Suomen sisällissota", "Finnish Civil War");
        var edits = Enumerable.Range(1, accountCount)
            .Select(i => MakeEdit(i, 77, 2020, $"User{i}", EditAction.Inserted))
            .ToList();

        var incidents = accountCount == 0
            ? Array.Empty<IncidentView>()
            : [MakeIncident("inc-size", EvidenceTier.Contested, [article], edits)];

        var graph = NetworkGraphBuilder.Build(incidents, NetworkFilter.Default);

        Assert.Equal(accountCount, graph.Accounts.Count);
        Assert.Equal(expectedTooSmall, graph.TooSmall);
    }

    private static EditView MakeEdit(long revId, long pageId, int year, string rawUser, EditAction action) =>
        new(
            RevId: revId,
            ParentRevId: revId - 1,
            PageId: pageId,
            Timestamp: new DateTimeOffset(year, 6, 15, 12, 0, 0, TimeSpan.Zero),
            Editor: EditorDisplay.For(rawUser),
            Summary: null,
            Action: action,
            Live: action == EditAction.Removed ? null : TimeSpan.FromHours(4));

    private static IncidentView MakeIncident(
        string id,
        EvidenceTier tier,
        IReadOnlyList<ArticleRef> articles,
        IReadOnlyList<EditView> edits,
        string? clusterId = null,
        string? sourceClusterId = null)
    {
        var firstTs = edits.Count > 0
            ? edits.Min(e => e.Timestamp)
            : new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);

        var source = new IncidentRecord
        {
            Id = id,
            PageId = articles[0].PageId,
            InsertingRevIds = edits.Where(e => e.Action != EditAction.Removed).Select(e => e.RevId).ToList(),
            RemovingRevIds = edits.Where(e => e.Action == EditAction.Removed).Select(e => e.RevId).ToList(),
            Tier = tier,
            Score = 0.8,
            ClusterId = sourceClusterId,
        };

        return new IncidentView(
            Id: id,
            Rank: 1,
            Tier: tier,
            TierReason: $"{EvidenceTiers.Get(tier).Label} — because: disputed removal.",
            ClaimEn: $"Claim for {id}",
            ClaimOriginal: "Väite",
            Lang: "fi",
            Kind: IncidentKind.Insertion,
            Articles: articles,
            FirstSeen: firstTs,
            LastRemoved: firstTs.AddHours(6),
            TotalLive: TimeSpan.FromHours(6),
            AccountCount: edits.Select(e => e.Editor.GroupingKey).Distinct(StringComparer.Ordinal).Count(),
            InsertCount: edits.Count(e => e.Action != EditAction.Removed),
            Signals: [],
            Edits: edits,
            Passage: null,
            Assessment: null,
            Relevance: 0.8,
            ClusterId: clusterId,
            Source: source);
    }
}
