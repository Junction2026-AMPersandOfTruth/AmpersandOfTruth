using System.Net;
using AmpersandOfTruth.Tests.Features.Investigations.Incident;
using AmpersandOfTruth.Tests.Infrastructure;
using AmpersandOfTruth.Web.Features.Accounts;
using AmpersandOfTruth.Web.Features.Corpus;
using AmpersandOfTruth.Web.Features.Evidence;
using AmpersandOfTruth.Web.Features.Investigations;
using AmpersandOfTruth.Web.Features.Investigations.Fixtures;
using AmpersandOfTruth.Web.Features.Investigations.View;
using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace AmpersandOfTruth.Tests.Features.Accounts;

public sealed class AccountPageTests : BunitContext
{
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 14, 12, 0, 0, TimeSpan.Zero));
    private readonly FakeAccountCorpus _fake = new();
    private readonly FakeEditorProfileSource _fakeProfile = new();

    public AccountPageTests()
    {
        Services.AddSingleton<TimeProvider>(_clock);
        Services.AddSingleton<IAccountCorpus>(sp => new CachedAccountCorpus(_fake, sp.GetRequiredService<TimeProvider>()));
        Services.AddSingleton<IEditorProfileSource>(sp => new CachedEditorProfileSource(_fakeProfile, sp.GetRequiredService<TimeProvider>()));
    }

    [Fact]
    public void Renders_facts_row_caveat_year_chart_and_article_links()
    {
        _fake.Accounts["Pohjantähti77"] = SampleFacts("Pohjantähti77");

        var cut = Render<AccountPage>(p => p.Add(x => x.Name, "Pohjantähti77"));

        var title = cut.FindComponent<PageTitle>();
        var titleMarkup = Render(title.Instance.ChildContent!).Markup;
        Assert.Contains("Account activity · Ampersand of Truth", titleMarkup);
        Assert.DoesNotContain("Pohjantähti77", titleMarkup, StringComparison.OrdinalIgnoreCase);

        var editorLabel = cut.Find("[data-testid=account-heading] [data-testid=editor-label]");
        var contrib = editorLabel.QuerySelector("a");
        Assert.NotNull(contrib);
        Assert.Equal("Pohjantähti77", contrib.TextContent.Trim());
        Assert.Equal(WikipediaLinks.Contributions("Pohjantähti77").AbsoluteUri, contrib.GetAttribute("href"));

        Assert.Equal(Methodology.AccountsCaveat, cut.Find("[data-testid=account-caveat]").TextContent.Trim());

        Assert.Equal(
            "1,204 edits in our dataset · 37 articles · 112 later removed · first 3 Mar 2009 · last 2 Feb 2024",
            cut.Find("[data-testid=account-facts]").TextContent.Trim());

        var chart = cut.Find("[data-testid=account-chart]");
        var bars = chart.QuerySelectorAll("rect[data-testid=year-bar]");
        Assert.Equal(3, bars.Length);
        Assert.Equal(bars.Length, chart.QuerySelectorAll("rect").Length);
        Assert.Equal(["2009", "2014", "2024"], bars.Select(b => b.GetAttribute("data-year") ?? "").ToArray());
        Assert.All(bars, b => Assert.Contains("--chart-named", b.GetAttribute("style")));

        var rows = cut.FindAll("[data-testid=account-articles] tbody tr");
        Assert.Equal(2, rows.Count);

        var firstLink = rows[0].QuerySelector("a")!;
        Assert.Equal("/articles/1256", firstLink.GetAttribute("href"));
        Assert.Equal("Talvisota", firstLink.TextContent);
        Assert.Contains("1,050", rows[0].TextContent);
        Assert.Contains("98", rows[0].TextContent);

        var secondLink = rows[1].QuerySelector("a")!;
        Assert.Equal("/articles/1150", secondLink.GetAttribute("href"));
        Assert.Equal("Suomen sisällissota <script>alert(1)</script>", secondLink.TextContent);
        Assert.Empty(cut.FindAll("script"));

        Assert.Empty(CopyRules.FindViolations(cut.Find("[data-testid=account-page]").TextContent, includeJargon: true));
    }

    [Theory]
    [InlineData("85.76.123.45")]
    [InlineData("2001:14bb:51:d813:a1b9:8f12:23b6:8d13")]
    [InlineData("~2025-26183-73")]
    public void Anonymous_or_temporary_editor_shows_empty_state_masks_ip_and_never_calls_corpus(string rawName)
    {
        var cut = Render<AccountPage>(p => p.Add(x => x.Name, rawName));

        var empty = cut.Find("[data-testid=empty-state]");
        Assert.Equal("We don't show pages for anonymous editors.", empty.QuerySelector("h3")?.TextContent);
        Assert.NotEmpty(empty.QuerySelector("p")?.TextContent?.Trim() ?? "");
        Assert.Empty(CopyRules.FindViolations(empty.TextContent, includeJargon: true));
        Assert.Equal(0, _fake.Calls);
        Assert.Equal(0, _fakeProfile.Calls);
        Assert.Empty(cut.FindAll("[data-testid=account-profile]"));
        Assert.DoesNotContain(rawName, cut.Markup, StringComparison.OrdinalIgnoreCase);

        var title = cut.FindComponent<PageTitle>();
        Assert.DoesNotContain(rawName, Render(title.Instance.ChildContent!).Markup, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MariaDbAccountCorpus_never_opens_connection_for_anonymous_input()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Corpus"] = "Server=127.0.0.1;Port=1;Database=corpus;User ID=x;Connection Timeout=1",
            })
            .Build();
        await using var factory = new CorpusConnectionFactory(config);
        var corpus = new MariaDbAccountCorpus(factory, NullLogger<MariaDbAccountCorpus>.Instance);

        var facts = await corpus.FactsAsync("85.76.123.45");

        Assert.Equal(0, facts.Edits);
        Assert.DoesNotContain("85.76.123.45", facts.User);
    }

    [Fact]
    public void Unknown_account_shows_empty_state()
    {
        var cut = Render<AccountPage>(p => p.Add(x => x.Name, "UnknownEditor99"));

        var empty = cut.Find("[data-testid=empty-state]");
        Assert.Equal("No edits by this account in our dataset.", empty.QuerySelector("h3")?.TextContent);
        Assert.NotEmpty(empty.QuerySelector("p")?.TextContent?.Trim() ?? "");
        Assert.Empty(CopyRules.FindViolations(empty.TextContent, includeJargon: true));
        Assert.Equal(1, _fake.Calls);
    }

    [Fact]
    public void Corpus_down_shows_error_banner_and_retry_recovers()
    {
        _fake.Accounts["Pohjantähti77"] = SampleFacts("Pohjantähti77");
        _fake.Unavailable = true;

        var cut = Render<AccountPage>(p => p.Add(x => x.Name, "Pohjantähti77"));

        var banner = cut.Find("[data-testid=status-banner]");
        Assert.Contains("aot-banner--error", banner.ClassList);
        Assert.Equal("alert", banner.GetAttribute("role"));
        Assert.Contains("We can’t reach the Wikipedia data right now.", banner.TextContent);
        Assert.Contains("fake corpus is down", banner.TextContent);
        Assert.Empty(cut.FindAll("[data-testid=account-facts]"));
        Assert.Equal(1, _fake.Calls);

        _fake.Unavailable = false;
        cut.Find("[data-testid=status-retry]").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Empty(cut.FindAll("[data-testid=status-banner]"));
            Assert.Contains("1,204 edits in our dataset", cut.Find("[data-testid=account-facts]").TextContent);
        });
        Assert.Equal(2, _fake.Calls);
    }

    [Fact]
    public void Second_load_within_one_hour_uses_in_memory_cache_without_calling_corpus()
    {
        _fake.Accounts["Pohjantähti77"] = SampleFacts("Pohjantähti77");

        var first = Render<AccountPage>(p => p.Add(x => x.Name, "Pohjantähti77"));
        Assert.Contains("1,204 edits", first.Find("[data-testid=account-facts]").TextContent);
        Assert.Equal(1, _fake.Calls);

        _clock.Advance(TimeSpan.FromMinutes(30));
        var second = Render<AccountPage>(p => p.Add(x => x.Name, "Pohjantähti77"));
        Assert.Contains("1,204 edits", second.Find("[data-testid=account-facts]").TextContent);
        Assert.Equal(1, _fake.Calls);

        _clock.Advance(TimeSpan.FromMinutes(31));
        var third = Render<AccountPage>(p => p.Add(x => x.Name, "Pohjantähti77"));
        Assert.Contains("1,204 edits", third.Find("[data-testid=account-facts]").TextContent);
        Assert.Equal(2, _fake.Calls);
    }

    [Fact]
    public async Task Route_returns_200_with_error_panel_when_corpus_is_absent()
    {
        await using var appFactory = new AppFactory();
        using var brokenFactory = appFactory.WithWebHostBuilder(b =>
            b.UseSetting("ConnectionStrings:Corpus", "Server=127.0.0.1;Port=1;Database=corpus;User ID=x;Connection Timeout=1"));
        using var client = brokenFactory.CreateClient();

        using var response = await client.GetAsync("/accounts/Example");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(
            """
            data-testid="status-banner"
            """,
            html);
    }

    [Fact]
    public void State_account_loading()
    {
        var pending = new PendingAccountCorpus();
        using var ctx = new BunitContext();
        ctx.Services.AddSingleton<TimeProvider>(_clock);
        ctx.Services.AddSingleton<IAccountCorpus>(pending);

        var cut = ctx.Render<AccountPage>(p => p.Add(x => x.Name, "Pohjantähti77"));

        var loading = cut.Find("[data-testid=account-loading]");
        Assert.Equal("status", loading.GetAttribute("role"));
        Assert.Contains("Loading account activity…", loading.TextContent);
    }

    [Fact]
    public void State_account_empty()
    {
        var cut = Render<AccountPage>(p => p.Add(x => x.Name, "UnseenEditor"));

        cut.WaitForAssertion(() =>
        {
            var empty = cut.Find("[data-testid=empty-state]");
            Assert.Contains("No edits by this account in our dataset.", empty.TextContent);
        });
    }

    [Fact]
    public void State_account_corpus_down()
    {
        _fake.Unavailable = true;

        var cut = Render<AccountPage>(p => p.Add(x => x.Name, "Pohjantähti77"));

        cut.WaitForAssertion(() =>
        {
            var banner = cut.Find("[data-testid=status-banner]");
            Assert.Contains("aot-banner--error", banner.ClassList);
            Assert.Contains("We can’t reach the Wikipedia data right now.", banner.TextContent);
            Assert.NotNull(cut.Find("[data-testid=status-retry]"));
        });
    }

    [Fact]
    public void State_account_embedder_down()
    {
        _fake.Accounts["Pohjantähti77"] = SampleFacts("Pohjantähti77");
        Services.AddSingleton<AmpersandOfTruth.Web.Features.Embedding.IQueryEmbedder>(
            new AmpersandOfTruth.Tests.Features.Embedding.FakeQueryEmbedder { Unavailable = true });

        var cut = Render<AccountPage>(p => p.Add(x => x.Name, "Pohjantähti77"));

        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(cut.Find("[data-testid=account-facts]"));
            Assert.Empty(cut.FindAll("[data-testid=status-banner]"));
        });
    }

    [Fact]
    public void State_account_llm_down()
    {
        _fake.Accounts["Pohjantähti77"] = SampleFacts("Pohjantähti77");
        Services.AddSingleton<AmpersandOfTruth.Web.Features.Analysis.IAnalysisModel>(
            new FakeAnalysisModel());

        var cut = Render<AccountPage>(p => p.Add(x => x.Name, "Pohjantähti77"));

        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(cut.Find("[data-testid=account-facts]"));
            Assert.Empty(cut.FindAll("[data-testid=status-banner]"));
        });
    }

    [Fact]
    public void State_account_slow()
    {
        var clock = new FakeTimeProvider();
        var pending = new PendingAccountCorpus();
        using var ctx = new BunitContext();
        ctx.Services.AddSingleton<TimeProvider>(clock);
        ctx.Services.AddSingleton<IAccountCorpus>(pending);

        var cut = ctx.Render<AccountPage>(p => p.Add(x => x.Name, "Pohjantähti77"));
        Assert.Equal("Loading account activity…", cut.Find("[data-testid=account-loading]").TextContent.Trim());

        clock.Advance(TimeSpan.FromSeconds(21));

        cut.WaitForAssertion(() =>
            Assert.Equal("Still loading account activity…", cut.Find("[data-testid=account-loading]").TextContent.Trim()));
    }

    [Fact]
    public void State_account_run_failed()
    {
        var throwing = new PendingAccountCorpus { Fault = new InvalidOperationException("unexpected account failure") };
        using var ctx = new BunitContext();
        ctx.Services.AddSingleton<TimeProvider>(_clock);
        ctx.Services.AddSingleton<IAccountCorpus>(throwing);

        var cut = ctx.Render<AccountPage>(p => p.Add(x => x.Name, "Pohjantähti77"));

        cut.WaitForAssertion(() =>
        {
            var banner = cut.Find("[data-testid=status-banner]");
            Assert.Contains("aot-banner--error", banner.ClassList);
            Assert.Contains("Something went wrong loading this account.", banner.TextContent);
            Assert.NotNull(cut.Find("[data-testid=status-retry]"));
        });
    }

    [Fact]
    public void Lists_matching_sample_incidents_across_topics_with_banner_compact_cards_and_max_6_while_non_matching_account_hides_section()
    {
        var t0 = new DateTimeOffset(2014, 3, 3, 14, 2, 0, TimeSpan.Zero);
        var incidentSource = new FakeIncidentSource()
            .WithTopic(
                "winter-war",
                Enumerable.Range(1, 5).Select(i => FakeIncidentSource.BuildIncident(
                    id: $"ww-0{i}",
                    rank: i,
                    edits: [new EditView(1000 + i, 900 + i, 1256, t0.AddDays(i), EditorDisplay.For("Pohjantähti77"), null, EditAction.Inserted, TimeSpan.FromDays(1))])).ToArray())
            .WithTopic(
                "continuation-war",
                [
                    FakeIncidentSource.BuildIncident(
                        id: "cw-01",
                        rank: 1,
                        edits: [new EditView(2001, 2000, 3000, t0.AddDays(10), EditorDisplay.For("Pohjantähti77"), null, EditAction.Removed, null)]),
                    FakeIncidentSource.BuildIncident(
                        id: "cw-02",
                        rank: 2,
                        edits: [new EditView(2002, 2001, 3000, t0.AddDays(11), EditorDisplay.For("Pohjantähti77"), null, EditAction.Inserted, TimeSpan.FromDays(1))]),
                ])
            .WithTopic(
                "civil-war-1918",
                [
                    FakeIncidentSource.BuildIncident(
                        id: "civ-01",
                        rank: 1,
                        edits: [new EditView(3001, 3000, 4000, t0.AddDays(20), EditorDisplay.For("OtherAccount"), null, EditAction.Inserted, TimeSpan.FromDays(1))]),
                ]);

        Services.AddSingleton(new SampleIncidents(NullLogger<SampleIncidents>.Instance));
        Services.AddSingleton<IIncidentSource>(incidentSource);
        Services.AddSingleton<IOptions<InvestigationSourceOptions>>(
            Options.Create(new InvestigationSourceOptions { Source = IncidentSourceKind.Fixtures }));

        _fake.Accounts["Pohjantähti77"] = SampleFacts("Pohjantähti77");
        _fake.Accounts["QuietEditor"] = SampleFacts("QuietEditor");

        var cut = Render<AccountPage>(p => p.Add(x => x.Name, "Pohjantähti77"));

        var section = cut.Find("[data-testid=account-incidents]");
        Assert.Equal("Incidents this account appears in", section.QuerySelector("h2")?.TextContent);
        Assert.NotNull(section.QuerySelector("[data-testid=sample-data-banner]"));

        // 7 matching incidents across continuation-war (2) + winter-war (5), capped at 6 compact IncidentCards
        var cards = section.QuerySelectorAll("[data-testid=incident-card]");
        Assert.Equal(6, cards.Length);
        var hrefs = cards.Select(static c => c.GetAttribute("href") ?? "").ToArray();
        Assert.Equal(
            [
                "/investigate/continuation-war/incidents/cw-01",
                "/investigate/continuation-war/incidents/cw-02",
                "/investigate/winter-war/incidents/ww-01",
                "/investigate/winter-war/incidents/ww-02",
                "/investigate/winter-war/incidents/ww-03",
                "/investigate/winter-war/incidents/ww-04",
            ],
            hrefs);

        // Compact cards omit the .aot-facts dl and signal chips
        Assert.All(cards, c => Assert.Null(c.QuerySelector("dl.aot-facts")));
        Assert.Empty(CopyRules.FindViolations(cut.Find("[data-testid=account-page]").TextContent, includeJargon: true));

        // Non-matching account -> no account-incidents section and no sample banner
        var quietCut = Render<AccountPage>(p => p.Add(x => x.Name, "QuietEditor"));
        Assert.Empty(quietCut.FindAll("[data-testid=account-incidents]"));
        Assert.Empty(quietCut.FindAll("[data-testid=sample-data-banner]"));
    }

    [Fact]
    public void Pipeline_mode_collects_matching_incidents_from_RecentRuns_without_sample_banner()
    {
        var t0 = new DateTimeOffset(2020, 6, 1, 10, 0, 0, TimeSpan.Zero);
        var recentRuns = new RecentRuns();
        recentRuns.Record("lapland-war", [new IncidentRecord { PageId = 500, InsertingRevIds = [1], RemovingRevIds = [2], Tier = EvidenceTier.Contested, Score = 0.8 }]);

        var incidentSource = new FakeIncidentSource()
            .WithTopic(
                "lapland-war",
                [
                    FakeIncidentSource.BuildIncident(
                        id: "lw-01",
                        rank: 1,
                        edits: [new EditView(501, 500, 500, t0, EditorDisplay.For("Pohjantähti77"), null, EditAction.Inserted, TimeSpan.FromDays(2))]),
                ]);

        Services.AddSingleton(recentRuns);
        Services.AddSingleton<IIncidentSource>(incidentSource);
        Services.AddSingleton<IOptions<InvestigationSourceOptions>>(
            Options.Create(new InvestigationSourceOptions { Source = IncidentSourceKind.Pipeline }));

        _fake.Accounts["Pohjantähti77"] = SampleFacts("Pohjantähti77");

        var cut = Render<AccountPage>(p => p.Add(x => x.Name, "Pohjantähti77"));

        var section = cut.Find("[data-testid=account-incidents]");
        Assert.Empty(section.QuerySelectorAll("[data-testid=sample-data-banner]"));
        var card = Assert.Single(section.QuerySelectorAll("[data-testid=incident-card]"));
        Assert.Equal("/investigate/lapland-war/incidents/lw-01", card.GetAttribute("href"));
    }

    [Fact]
    public async Task Pipeline_mode_lists_incident_from_cached_run_and_unions_with_RecentRuns()
    {
        using var db = new TestDb();
        var pipelineOpts = Options.Create(new AmpersandOfTruth.Web.Features.Investigations.Pipeline.InvestigationPipelineOptions());
        var geminiOpts = Options.Create(new AmpersandOfTruth.Web.Features.Analysis.GeminiOptions());
        var fingerprint = new AmpersandOfTruth.Web.Features.Investigations.Cache.PipelineFingerprint(
            pipelineOpts,
            geminiOpts,
            new AmpersandOfTruth.Web.Features.Analysis.Prompts.PromptLibrary(
                geminiOpts,
                NullLogger<AmpersandOfTruth.Web.Features.Analysis.Prompts.PromptLibrary>.Instance));
        var runCache = new AmpersandOfTruth.Web.Features.Investigations.Cache.InvestigationRunCache(
            db.Factory,
            fingerprint,
            _clock,
            NullLogger<AmpersandOfTruth.Web.Features.Investigations.Cache.InvestigationRunCache>.Instance);

        var t0 = new DateTimeOffset(2020, 6, 1, 10, 0, 0, TimeSpan.Zero);
        var cachedRecord = new IncidentRecord
        {
            Id = "ww-cached-01",
            PageId = 1256,
            InsertingRevIds = [501],
            RemovingRevIds = [502],
            Tier = EvidenceTier.Contested,
            Score = 0.85,
            ClaimEn = "Cached pipeline incident claim.",
            Edits =
            [
                new IncidentEdit(501, 500, 1256, "Pohjantähti77", false, t0, IncidentEditRole.Inserted, null, null, []),
                new IncidentEdit(502, 501, 1256, "OtherEditor", false, t0.AddDays(2), IncidentEditRole.Removed, null, null, []),
            ],
        };

        await runCache.StoreAsync(
            TopicSlug.ToQuery("winter-war"),
            new AmpersandOfTruth.Web.Features.Investigations.Cache.RunSnapshot(
                Articles: [],
                RevisionsScanned: 10,
                Candidates: [],
                Scored: [],
                Incidents: [cachedRecord],
                AnalysedAt: _clock.GetUtcNow(),
                ElapsedMs: 120,
                StageMs: new Dictionary<AmpersandOfTruth.Web.Features.Investigations.Pipeline.InvestigationStage, double>(),
                FirstIncidentMs: 60));

        var recentRuns = new RecentRuns();
        var mapper = new IncidentMapper(
            new AmpersandOfTruth.Tests.Features.Investigations.View.FakeIncidentCorpus { Unavailable = true },
            new AmpersandOfTruth.Tests.Features.Investigations.FakeRevisionStore { Unavailable = true });
        var incidentSource = new RecentRunIncidentSource(recentRuns, mapper, runCache);

        Services.AddSingleton(recentRuns);
        Services.AddSingleton(runCache);
        Services.AddSingleton<IIncidentSource>(incidentSource);
        Services.AddSingleton<IOptions<InvestigationSourceOptions>>(
            Options.Create(new InvestigationSourceOptions { Source = IncidentSourceKind.Pipeline }));

        _fake.Accounts["Pohjantähti77"] = SampleFacts("Pohjantähti77");

        // 1. Empty RecentRuns + cached run -> Account page lists the cached incident.
        var cut = Render<AccountPage>(p => p.Add(x => x.Name, "Pohjantähti77"));

        cut.WaitForAssertion(() =>
        {
            var section = cut.Find("[data-testid=account-incidents]");
            Assert.Empty(section.QuerySelectorAll("[data-testid=sample-data-banner]"));
            var card = Assert.Single(section.QuerySelectorAll("[data-testid=incident-card]"));
            Assert.Equal("/investigate/winter-war/incidents/ww-cached-01", card.GetAttribute("href"));
        });

        // 2. Also add an in-memory-only topic to RecentRuns -> union shows both without duplicating winter-war.
        var inMemoryRecord = cachedRecord with
        {
            Id = "lw-mem-01",
            PageId = 500,
            InsertingRevIds = [601],
            RemovingRevIds = [602],
        };
        recentRuns.Record("lapland-war", [inMemoryRecord]);

        var unionCut = Render<AccountPage>(p => p.Add(x => x.Name, "Pohjantähti77"));
        unionCut.WaitForAssertion(() =>
        {
            var hrefs = unionCut.FindAll("[data-testid=account-incidents] [data-testid=incident-card]")
                .Select(static c => c.GetAttribute("href") ?? "")
                .ToArray();
            Assert.Equal(
                [
                    "/investigate/lapland-war/incidents/lw-mem-01",
                    "/investigate/winter-war/incidents/ww-cached-01",
                ],
                hrefs);
        });
    }

    [Fact]
    public async Task MariaDbEditorProfileSource_never_opens_connection_for_anonymous_input()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Corpus"] = "Server=127.0.0.1;Port=1;Database=corpus;User ID=x;Connection Timeout=1",
            })
            .Build();
        await using var factory = new CorpusConnectionFactory(config);
        var source = new MariaDbEditorProfileSource(factory, NullLogger<MariaDbEditorProfileSource>.Instance);

        Assert.Null(await source.ProfileAsync("85.76.123.45"));
        Assert.Null(await source.ProfileAsync("2001:14bb:51:d813:a1b9:8f12:23b6:8d13"));
        Assert.Null(await source.ProfileAsync("~2025-26183-73"));
    }

    [Fact]
    public void Profile_section_renders_all_lines_top_page_link_and_passes_copy_rules()
    {
        _fake.Accounts["Pohjantähti77"] = SampleFacts("Pohjantähti77");
        _fakeProfile.Profiles["Pohjantähti77"] = new EditorProfile(
            User: "Pohjantähti77",
            IsBot: true,
            FirstTs: new DateTime(2009, 3, 3, 14, 2, 0, DateTimeKind.Utc),
            LastTs: new DateTime(2024, 2, 2, 9, 15, 0, DateTimeKind.Utc),
            Edits: 12,
            NonNullEdits: 12,
            ActiveDays: 214,
            TopPageId: 1256,
            TopPageTitle: "Talvisota",
            TopPageShare: 0.62,
            RevertedEditsByOthers: 9,
            RevertsMadeOthers: 40,
            CustomSummaryUsage: 0.35);

        var cut = Render<AccountPage>(p => p.Add(x => x.Name, "Pohjantähti77"));

        var section = cut.Find("[data-testid=account-profile]");
        Assert.Equal("Activity in our dataset", section.QuerySelector("h2")?.TextContent);

        var list = section.QuerySelector("ul");
        Assert.NotNull(list);
        Assert.Contains("aot-muted", list.ClassList);

        var items = section.QuerySelectorAll("li").Select(li => li.TextContent.Trim()).ToArray();
        Assert.Equal(
            [
                "Active on 214 days between 3 Mar 2009 and 2 Feb 2024.",
                "62% of its edits are on one article: Talvisota.",
                "9 of 12 edits were later removed by other editors.",
                "It removed 40 edits by other editors.",
                "Wrote its own edit summary on 35% of edits.",
                "Marked as a bot account.",
            ],
            items);

        var topLink = section.QuerySelector("li a");
        Assert.NotNull(topLink);
        Assert.Equal("/articles/1256", topLink.GetAttribute("href"));
        Assert.Equal("fi", topLink.GetAttribute("lang"));
        Assert.Equal("Talvisota", topLink.TextContent);

        Assert.Empty(CopyRules.FindViolations(cut.Find("[data-testid=account-page]").TextContent, includeJargon: true));
    }

    [Fact]
    public void Profile_section_shows_or_omits_each_line_by_data_formats_thousands_and_encodes_title()
    {
        var baseProfile = new EditorProfile(
            User: "Pohjantähti77",
            IsBot: true,
            FirstTs: new DateTime(2009, 3, 3, 0, 0, 0, DateTimeKind.Utc),
            LastTs: new DateTime(2024, 2, 2, 0, 0, 0, DateTimeKind.Utc),
            Edits: 12_345,
            NonNullEdits: 12_345,
            ActiveDays: 1_214,
            TopPageId: 1150,
            TopPageTitle: "Suomen sisällissota <script>alert(1)</script>",
            TopPageShare: 0.5,
            RevertedEditsByOthers: 1_050,
            RevertsMadeOthers: 1_200,
            CustomSummaryUsage: 0.35);

        // 1. Thousands separators + XSS-safe top-page title at exact 0.5 boundary
        _fake.Accounts["UserFull"] = SampleFacts("UserFull");
        _fakeProfile.Profiles["UserFull"] = baseProfile with { User = "UserFull" };
        var fullCut = Render<AccountPage>(p => p.Add(x => x.Name, "UserFull"));
        var fullSection = fullCut.Find("[data-testid=account-profile]");
        var fullLines = fullSection.QuerySelectorAll("li").Select(li => li.TextContent.Trim()).ToArray();
        Assert.Equal(
            [
                "Active on 1,214 days between 3 Mar 2009 and 2 Feb 2024.",
                "50% of its edits are on one article: Suomen sisällissota <script>alert(1)</script>.",
                "1,050 of 12,345 edits were later removed by other editors.",
                "It removed 1,200 edits by other editors.",
                "Wrote its own edit summary on 35% of edits.",
                "Marked as a bot account.",
            ],
            fullLines);
        Assert.Empty(fullCut.FindAll("script"));
        Assert.Empty(CopyRules.FindViolations(fullSection.TextContent, includeJargon: true));

        // 2. Active-days line absent when single-day; top-page line absent when share < 0.5; bot line absent when !IsBot
        _fake.Accounts["UserPartialA"] = SampleFacts("UserPartialA");
        _fakeProfile.Profiles["UserPartialA"] = baseProfile with
        {
            User = "UserPartialA",
            ActiveDays = 1,
            FirstTs = new DateTime(2024, 2, 2, 8, 0, 0, DateTimeKind.Utc),
            LastTs = new DateTime(2024, 2, 2, 9, 0, 0, DateTimeKind.Utc),
            TopPageShare = 0.49,
            IsBot = false,
        };
        var partialACut = Render<AccountPage>(p => p.Add(x => x.Name, "UserPartialA"));
        var partialALines = partialACut.Find("[data-testid=account-profile]").QuerySelectorAll("li").Select(li => li.TextContent.Trim()).ToArray();
        Assert.Equal(
            [
                "1,050 of 12,345 edits were later removed by other editors.",
                "It removed 1,200 edits by other editors.",
                "Wrote its own edit summary on 35% of edits.",
            ],
            partialALines);
        Assert.Empty(CopyRules.FindViolations(partialACut.Find("[data-testid=account-profile]").TextContent, includeJargon: true));

        // 3. Removed-by-others, removed-others, and custom-summary lines absent when 0; top-page absent when title missing
        _fake.Accounts["UserPartialB"] = SampleFacts("UserPartialB");
        _fakeProfile.Profiles["UserPartialB"] = baseProfile with
        {
            User = "UserPartialB",
            TopPageTitle = null,
            RevertedEditsByOthers = 0,
            RevertsMadeOthers = 0,
            CustomSummaryUsage = 0.0,
            IsBot = false,
        };
        var partialBCut = Render<AccountPage>(p => p.Add(x => x.Name, "UserPartialB"));
        var partialBLines = partialBCut.Find("[data-testid=account-profile]").QuerySelectorAll("li").Select(li => li.TextContent.Trim()).ToArray();
        Assert.Equal(["Active on 1,214 days between 3 Mar 2009 and 2 Feb 2024."], partialBLines);
        Assert.Empty(CopyRules.FindViolations(partialBCut.Find("[data-testid=account-profile]").TextContent, includeJargon: true));

        // 4. All lines unsupported -> whole section hidden, no banner
        _fake.Accounts["UserNone"] = SampleFacts("UserNone");
        _fakeProfile.Profiles["UserNone"] = new EditorProfile(
            User: "UserNone",
            IsBot: false,
            FirstTs: new DateTime(2024, 2, 2, 8, 0, 0, DateTimeKind.Utc),
            LastTs: new DateTime(2024, 2, 2, 8, 5, 0, DateTimeKind.Utc),
            Edits: 2,
            NonNullEdits: 2,
            ActiveDays: 1,
            TopPageId: 1256,
            TopPageTitle: "Talvisota",
            TopPageShare: 0.4,
            RevertedEditsByOthers: 0,
            RevertsMadeOthers: 0,
            CustomSummaryUsage: 0.0);
        var noneCut = Render<AccountPage>(p => p.Add(x => x.Name, "UserNone"));
        Assert.Empty(noneCut.FindAll("[data-testid=account-profile]"));
        Assert.Empty(noneCut.FindAll("[data-testid=status-banner]"));
    }

    [Fact]
    public void Profile_lookup_failure_or_missing_row_hides_section_without_banner_and_caches_only_clean_responses()
    {
        _fake.Accounts["Pohjantähti77"] = SampleFacts("Pohjantähti77");
        _fakeProfile.Unavailable = true;

        var failedCut = Render<AccountPage>(p => p.Add(x => x.Name, "Pohjantähti77"));
        Assert.NotNull(failedCut.Find("[data-testid=account-facts]"));
        Assert.Empty(failedCut.FindAll("[data-testid=account-profile]"));
        Assert.Empty(failedCut.FindAll("[data-testid=status-banner]"));
        Assert.Equal(1, _fakeProfile.Calls);

        // Failure was not cached -> recovery succeeds immediately and is then cached for 1h
        _fakeProfile.Unavailable = false;
        _fakeProfile.Profiles["Pohjantähti77"] = new EditorProfile(
            User: "Pohjantähti77",
            IsBot: true,
            FirstTs: null,
            LastTs: null,
            Edits: 5,
            NonNullEdits: 5,
            ActiveDays: 1,
            TopPageId: null,
            TopPageTitle: null,
            TopPageShare: 0,
            RevertedEditsByOthers: 0,
            RevertsMadeOthers: 0,
            CustomSummaryUsage: 0);

        var recoveredCut = Render<AccountPage>(p => p.Add(x => x.Name, "Pohjantähti77"));
        Assert.Equal("Marked as a bot account.", recoveredCut.Find("[data-testid=account-profile] li").TextContent.Trim());
        Assert.Equal(2, _fakeProfile.Calls);

        _clock.Advance(TimeSpan.FromMinutes(30));
        var cachedCut = Render<AccountPage>(p => p.Add(x => x.Name, "Pohjantähti77"));
        Assert.Equal("Marked as a bot account.", cachedCut.Find("[data-testid=account-profile] li").TextContent.Trim());
        Assert.Equal(2, _fakeProfile.Calls);

        // Missing profile row (e.g. absent s6_editor_profile table) also hides section with no banner
        _fake.Accounts["NoProfileUser"] = SampleFacts("NoProfileUser");
        var missingCut = Render<AccountPage>(p => p.Add(x => x.Name, "NoProfileUser"));
        Assert.NotNull(missingCut.Find("[data-testid=account-facts]"));
        Assert.Empty(missingCut.FindAll("[data-testid=account-profile]"));
        Assert.Empty(missingCut.FindAll("[data-testid=status-banner]"));
    }

    private sealed class PendingAccountCorpus : IAccountCorpus
    {
        private readonly TaskCompletionSource<AccountFacts> _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Exception? Fault { get; init; }

        public Task<AccountFacts> FactsAsync(string user, CancellationToken ct = default) =>
            Fault is not null
                ? Task.FromException<AccountFacts>(Fault)
                : _tcs.Task.WaitAsync(ct);
    }

    private static AccountFacts SampleFacts(string user) => new(
        User: user,
        Edits: 1_204,
        Articles: 37,
        RemovedEdits: 112,
        First: new DateTime(2009, 3, 3, 14, 2, 0, DateTimeKind.Utc),
        Last: new DateTime(2024, 2, 2, 9, 15, 0, DateTimeKind.Utc),
        PerYear: [(2009, 300), (2014, 500), (2024, 404)],
        TopArticles:
        [
            new AccountArticle(1256, "Talvisota", 1_050, 98),
            new AccountArticle(1150, "Suomen sisällissota <script>alert(1)</script>", 154, 14),
        ]);
}
