using System.Runtime.CompilerServices;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using AmpersandOfTruth.Tests.Features.Articles;
using AmpersandOfTruth.Tests.Features.Investigations;
using AmpersandOfTruth.Tests.Features.Investigations.Incident;
using AmpersandOfTruth.Tests.Features.Investigations.View;
using AmpersandOfTruth.Tests.Infrastructure;
using AmpersandOfTruth.Web.Features.Articles;
using AmpersandOfTruth.Web.Features.Evidence;
using AmpersandOfTruth.Web.Features.Investigations;
using AmpersandOfTruth.Web.Features.Investigations.Overview;
using AmpersandOfTruth.Web.Features.Investigations.Pipeline;
using AmpersandOfTruth.Web.Features.Investigations.View;
using AmpersandOfTruth.Web.Features.Library;
using AmpersandOfTruth.Web.Features.Network;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace AmpersandOfTruth.Tests.Features.Network;

public sealed class NetworkPageTests : BunitContext
{
    private readonly FakeIncidentSource _source = new();

    public NetworkPageTests()
    {
        Services.AddSingleton<IIncidentSource>(_source);
        Services.AddSingleton<IOptions<InvestigationSourceOptions>>(
            Options.Create(new InvestigationSourceOptions { Source = IncidentSourceKind.Fixtures }));
        Services.AddNetworkFeature();
    }

    [Fact]
    public void Matrix_renders_rows_cells_links_tooltips_named_account_links_unlinked_ip_rows_and_passes_CopyRules()
    {
        _source.WithTopic("winter-war", BuildMatrixIncidents());

        var nav = Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo("/investigate/winter-war/network");

        var cut = Render<NetworkPage>(p => p.Add(x => x.Topic, "winter-war"));

        cut.WaitForElement("[data-testid=network-matrix]");
        Assert.Empty(cut.FindAll("[data-testid=network-list]"));
        Assert.Empty(cut.FindAll("[data-testid=network-empty]"));

        // Caveats present (both main caveat and anonymous prefix caveat)
        var caveat = cut.Find("[data-testid=network-caveat]").TextContent;
        Assert.Contains(
            "Accounts that appear together here edited the same articles. That is a prompt to read the edits, not a finding about the accounts.",
            caveat);
        Assert.Contains(
            "Anonymous addresses are grouped by masked prefix; one prefix can be many people.",
            caveat);

        // 4 accounts: NamedAlpha (2 articles), 85.76.x.x (1 article, 2 edits), NamedBeta (1 article), 2001:14BA:… (1 article)
        var rows = cut.FindAll("[data-testid=network-row]");
        Assert.Equal(4, rows.Count);

        // Named account row header links to /accounts/{name}
        var namedRow = rows.Single(r => r.GetAttribute("data-account") == "user:NamedAlpha");
        var namedHeaderLink = Assert.Single(namedRow.QuerySelectorAll("th a"));
        Assert.Equal("/accounts/NamedAlpha", namedHeaderLink.GetAttribute("href"));

        // IPv4 and IPv6 rows NEVER link in row header
        var ipv4Row = rows.Single(r => r.GetAttribute("data-account") == "ip4:85.76");
        Assert.Empty(ipv4Row.QuerySelectorAll("th a"));
        Assert.Contains("85.76.x.x", ipv4Row.QuerySelector("th")!.TextContent);

        var ipv6Row = rows.Single(r => r.GetAttribute("data-account") == "ip6:2001:14BA");
        Assert.Empty(ipv6Row.QuerySelectorAll("th a"));

        // Cells link to first incident and include counts in title + non-colour glyphs
        var cells = cut.FindAll("[data-testid=network-cell]");
        Assert.Equal(5, cells.Count);

        var anonCell = cells.Single(c => c.GetAttribute("data-account") == "ip4:85.76" && c.GetAttribute("data-page-id") == "1150");
        Assert.Equal("/investigate/winter-war/incidents/ww-01", anonCell.GetAttribute("href"));
        Assert.Contains("1 inserted", anonCell.GetAttribute("title"));
        Assert.Contains("1 re-inserted", anonCell.GetAttribute("title"));
        Assert.Contains("+↻", anonCell.TextContent);

        var removerCell = cells.Single(c => c.GetAttribute("data-account") == "user:NamedBeta" && c.GetAttribute("data-page-id") == "1150");
        Assert.Equal("/investigate/winter-war/incidents/ww-01", removerCell.GetAttribute("href"));
        Assert.Contains("1 removed", removerCell.GetAttribute("title"));
        Assert.Contains("−", removerCell.TextContent);

        AssertPassesCopyRules(cut);
    }

    [Fact]
    public void Small_graph_renders_plain_list_instead_of_matrix_and_only_links_named_accounts()
    {
        var smallIncidents = new[]
        {
            FakeIncidentSource.BuildIncident(
                id: "sm-01",
                articles: [new ArticleRef(1150, "Talvisota", "Winter War")],
                edits:
                [
                    new EditView(101, 100, 1150, new DateTimeOffset(2018, 4, 1, 0, 0, 0, TimeSpan.Zero), EditorDisplay.For("85.76.10.20"), null, EditAction.Inserted, TimeSpan.FromHours(3)),
                    new EditView(102, 101, 1150, new DateTimeOffset(2018, 4, 1, 3, 0, 0, TimeSpan.Zero), EditorDisplay.For("ReviewerNamed"), null, EditAction.Removed, null),
                ]),
        };
        _source.WithTopic("small-topic", smallIncidents);

        var cut = Render<NetworkPage>(p => p.Add(x => x.Topic, "small-topic"));

        var list = cut.WaitForElement("[data-testid=network-list]");
        Assert.Empty(cut.FindAll("[data-testid=network-matrix]"));
        Assert.Empty(cut.FindAll("[data-testid=network-empty]"));
        Assert.NotNull(cut.Find("[data-testid=network-caveat]"));

        var items = list.QuerySelectorAll("[data-account]");
        Assert.Equal(2, items.Length);

        var namedItem = items.Single(i => i.GetAttribute("data-account") == "user:ReviewerNamed");
        var accountLink = Assert.Single(namedItem.QuerySelectorAll(".network-list__account a"));
        Assert.Equal("/accounts/ReviewerNamed", accountLink.GetAttribute("href"));

        var ipItem = items.Single(i => i.GetAttribute("data-account") == "ip4:85.76");
        Assert.Empty(ipItem.QuerySelectorAll(".network-list__account a"));
        Assert.Contains(
            ipItem.QuerySelectorAll("a"),
            a => a.GetAttribute("href") == "/investigate/small-topic/incidents/sm-01");

        AssertPassesCopyRules(cut);
    }

    [Fact]
    public void Empty_state_renders_when_topic_is_unloaded_with_link_to_open_investigation()
    {
        var cut = Render<NetworkPage>(p => p.Add(x => x.Topic, "unloaded-topic"));

        var empty = cut.WaitForElement("[data-testid=network-empty]");
        Assert.Empty(cut.FindAll("[data-testid=network-matrix]"));
        Assert.Empty(cut.FindAll("[data-testid=network-list]"));

        var openLink = empty.QuerySelector("a[href='/investigate/unloaded-topic']");
        Assert.NotNull(openLink);
    }

    [Fact]
    public void Filters_update_query_string_and_narrow_graph()
    {
        _source.WithTopic("winter-war", BuildMatrixIncidents());

        var nav = Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo("/investigate/winter-war/network");

        var cut = Render<NetworkPage>(p => p.Add(x => x.Topic, "winter-war"));
        cut.WaitForElement("[data-testid=network-matrix]");

        // Toggle "Show removers" off -> NamedBeta (remover-only) disappears, leaving 3 accounts -> switches to network-list
        cut.Find("[data-testid=network-filter-removers]").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("removers=0", nav.Uri);
            Assert.NotNull(cut.Find("[data-testid=network-list]"));
            Assert.Empty(cut.FindAll("[data-account='user:NamedBeta']"));
        });

        // Toggle "Show removers" back on -> matrix returns
        cut.Find("[data-testid=network-filter-removers]").Click();
        cut.WaitForElement("[data-testid=network-matrix]");

        // Filter minimum level to Campaign (3) -> only ww-02 (Campaign) remains
        cut.Find("[data-testid=network-filter-tier]").Change("3");
        cut.WaitForAssertion(() =>
        {
            Assert.Contains("tier=3", nav.Uri);
            var list = cut.Find("[data-testid=network-list]");
            Assert.Contains("NamedAlpha", list.TextContent);
            Assert.DoesNotContain("NamedBeta", list.TextContent);
        });

        // Clear filters, then narrow year range to 2014 -> excludes 2019 ww-02
        cut.Find("[data-testid=network-clear-filters]").Click();
        cut.WaitForElement("[data-testid=network-matrix]");

        cut.Find("[data-testid=network-to-year]").Change("2015");
        cut.WaitForAssertion(() =>
        {
            Assert.Contains("to=2015", nav.Uri);
            Assert.NotNull(cut.Find("[data-testid=network-list]"));
        });
    }

    [Fact]
    public void Cap_note_renders_when_graph_exceeds_40_accounts_or_25_articles()
    {
        var articles = Enumerable.Range(1, 27)
            .Select(i => new ArticleRef(i, $"Artikkeli {i}", null))
            .ToList();
        var edits = Enumerable.Range(1, 42)
            .Select(i => new EditView(
                i,
                i - 1,
                ((i - 1) % 27) + 1,
                new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero),
                EditorDisplay.For($"Editor_{i:D2}"),
                null,
                EditAction.Inserted,
                TimeSpan.FromHours(1)))
            .ToList();

        _source.WithTopic("large-topic",
        [
            FakeIncidentSource.BuildIncident(
                id: "lg-01",
                articles: articles,
                edits: edits),
        ]);

        var cut = Render<NetworkPage>(p => p.Add(x => x.Topic, "large-topic"));

        cut.WaitForElement("[data-testid=network-matrix]");
        Assert.Equal(40, cut.FindAll("[data-testid=network-row]").Count);

        var capNote = cut.Find("[data-testid=network-cap-note]").TextContent;
        Assert.Contains("2 accounts", capNote);
        Assert.Contains("2 articles left out", capNote);
    }

    [Fact]
    public async Task Saved_investigation_loads_from_LibraryStore_and_preserves_saved_query_on_links_and_overview_header()
    {
        using var db = new TestDb();
        await using var ctx = new BunitContext();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;

        var corpus = new FakeIncidentCorpus();
        corpus.Articles[1150] = new ArticleRef(1150, "Talvisota", "Winter War");
        corpus.Articles[1200] = new ArticleRef(1200, "Mainilan laukaukset", "Shelling of Mainila");

        db.AddTo(ctx.Services);
        ctx.Services.AddSingleton<IInvestigationRunner>(new EmptyRunner());
        ctx.Services.AddSingleton<IIncidentCorpus>(corpus);
        ctx.Services.AddSingleton<IRevisionStore>(new FakeRevisionStore());
        ctx.Services.AddSingleton<IncidentMapper>();
        ctx.Services.AddSingleton(new RecentRuns());
        ctx.Services.AddSingleton<IIncidentSource>(new FakeIncidentSource());
        ctx.Services.AddSingleton<IOptions<InvestigationSourceOptions>>(
            Options.Create(new InvestigationSourceOptions { Source = IncidentSourceKind.Pipeline }));
        ctx.Services.AddSingleton<TimeProvider>(new FakeTimeProvider(new DateTimeOffset(2026, 11, 14, 12, 0, 0, TimeSpan.Zero)));
        ctx.Services.AddSingleton<IArticleCorpus>(new FakeArticleCorpus());
        ctx.Services.AddSingleton<ArticleTimelineService>();
        ctx.Services.AddLibraryFeature();
        ctx.Services.AddNetworkFeature();

        var store = ctx.Services.GetRequiredService<LibraryStore>();
        var savedRecord = new IncidentRecord
        {
            Id = "saved-inc-1",
            PageId = 1150,
            InsertingRevIds = [101, 103],
            RemovingRevIds = [102, 104],
            Tier = EvidenceTier.Campaign,
            Score = 0.9,
            Articles = [new IncidentArticle(1150, "Talvisota", "Winter War")],
            Edits =
            [
                new IncidentEdit(101, 100, 1150, "AlphaUser", false, new DateTimeOffset(2019, 1, 1, 0, 0, 0, TimeSpan.Zero), IncidentEditRole.Inserted, null, null, []),
                new IncidentEdit(102, 101, 1150, "BetaUser", false, new DateTimeOffset(2019, 1, 2, 0, 0, 0, TimeSpan.Zero), IncidentEditRole.Removed, null, null, []),
                new IncidentEdit(103, 102, 1150, "85.76.x.x", true, new DateTimeOffset(2019, 1, 3, 0, 0, 0, TimeSpan.Zero), IncidentEditRole.Reinserted, null, null, []),
                new IncidentEdit(104, 103, 1150, "DeltaUser", false, new DateTimeOffset(2019, 1, 4, 0, 0, 0, TimeSpan.Zero), IncidentEditRole.Removed, null, null, []),
            ],
        };

        var savedId = await store.SaveAsync(
            "owner-1",
            "Saved Winter War",
            new SavedSnapshot(
                "Winter War",
                [new TopicArticle("fi", 1150, "Talvisota", 0.05)],
                [savedRecord],
                new DateTimeOffset(2026, 11, 14, 10, 0, 0, TimeSpan.Zero)));

        var nav = ctx.Services.GetRequiredService<NavigationManager>();

        // 1. Check InvestigationPage header link without and with ?saved=
        nav.NavigateTo("/investigate/winter-war");
        var overviewPlain = ctx.Render<InvestigationPage>(p => p.Add(x => x.Topic, "winter-war"));
        Assert.Equal(
            "/investigate/winter-war/network",
            overviewPlain.Find("[data-testid=network-link]").GetAttribute("href"));

        nav.NavigateTo($"/investigate/winter-war?saved={savedId}");
        var overviewSaved = ctx.Render<InvestigationPage>(p => p
            .Add(x => x.Topic, "winter-war"));
        Assert.Equal(
            $"/investigate/winter-war/network?saved={savedId}",
            overviewSaved.Find("[data-testid=network-link]").GetAttribute("href"));

        // 2. Render NetworkPage with ?saved={savedId}
        nav.NavigateTo($"/investigate/winter-war/network?saved={savedId}");
        var networkCut = ctx.Render<NetworkPage>(p => p
            .Add(x => x.Topic, "winter-war"));

        networkCut.WaitForElement("[data-testid=network-matrix]");
        var cells = networkCut.FindAll("[data-testid=network-cell]");
        Assert.Equal(4, cells.Count);
        Assert.All(cells, c =>
            Assert.Equal($"/investigate/winter-war/incidents/saved-inc-1?saved={savedId}", c.GetAttribute("href")));
    }

    private static IReadOnlyList<IncidentView> BuildMatrixIncidents()
    {
        var artMain = new ArticleRef(1150, "Talvisota", "Winter War");
        var artSub = new ArticleRef(1200, "Mainilan laukaukset", "Shelling of Mainila");

        return
        [
            FakeIncidentSource.BuildIncident(
                id: "ww-01",
                rank: 1,
                tier: EvidenceTier.Contested,
                articles: [artMain],
                edits:
                [
                    new EditView(1001, 1000, 1150, new DateTimeOffset(2014, 3, 1, 0, 0, 0, TimeSpan.Zero), EditorDisplay.For("NamedAlpha"), null, EditAction.Inserted, TimeSpan.FromHours(5)),
                    new EditView(1002, 1001, 1150, new DateTimeOffset(2014, 3, 1, 5, 0, 0, TimeSpan.Zero), EditorDisplay.For("NamedBeta"), null, EditAction.Removed, null),
                    new EditView(1003, 1002, 1150, new DateTimeOffset(2014, 3, 2, 0, 0, 0, TimeSpan.Zero), EditorDisplay.For("85.76.12.34"), null, EditAction.Inserted, TimeSpan.FromHours(2)),
                    new EditView(1004, 1003, 1150, new DateTimeOffset(2014, 3, 2, 3, 0, 0, TimeSpan.Zero), EditorDisplay.For("85.76.99.1"), null, EditAction.Reinserted, TimeSpan.FromHours(2)),
                ]),
            FakeIncidentSource.BuildIncident(
                id: "ww-02",
                rank: 2,
                tier: EvidenceTier.Campaign,
                articles: [artSub],
                edits:
                [
                    new EditView(2001, 2000, 1200, new DateTimeOffset(2019, 6, 10, 0, 0, 0, TimeSpan.Zero), EditorDisplay.For("NamedAlpha"), null, EditAction.Inserted, TimeSpan.FromHours(8)),
                    new EditView(2002, 2001, 1200, new DateTimeOffset(2019, 6, 10, 8, 0, 0, TimeSpan.Zero), EditorDisplay.For("2001:14ba:1:2::3"), null, EditAction.Reinserted, TimeSpan.FromHours(4)),
                ]),
        ];
    }

    private static void AssertPassesCopyRules(IRenderedComponent<NetworkPage> cut)
    {
        var doc = new HtmlParser().ParseDocument(cut.Markup);
        var renderedText = string.Join(
            " ",
            doc.Descendants<IText>().Select(t => t.Text).Where(t => !string.IsNullOrWhiteSpace(t)));
        Assert.Empty(CopyRules.FindViolations(renderedText, includeJargon: true));

        foreach (var el in cut.FindAll("[title], [aria-label]"))
        {
            var title = el.GetAttribute("title");
            if (!string.IsNullOrWhiteSpace(title))
            {
                Assert.Empty(CopyRules.FindViolations(title, includeJargon: true));
            }

            var aria = el.GetAttribute("aria-label");
            if (!string.IsNullOrWhiteSpace(aria))
            {
                Assert.Empty(CopyRules.FindViolations(aria, includeJargon: true));
            }
        }
    }

    private sealed class EmptyRunner : IInvestigationRunner
    {
        public async IAsyncEnumerable<InvestigationEvent> RunAsync(
            string topic,
            bool refresh = false,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.CompletedTask;
            yield break;
        }
    }
}
