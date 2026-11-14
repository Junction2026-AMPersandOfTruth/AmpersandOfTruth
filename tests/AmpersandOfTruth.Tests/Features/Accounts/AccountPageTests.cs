using System.Net;
using AmpersandOfTruth.Tests.Infrastructure;
using AmpersandOfTruth.Web.Features.Accounts;
using AmpersandOfTruth.Web.Features.Corpus;
using AmpersandOfTruth.Web.Features.Evidence;
using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace AmpersandOfTruth.Tests.Features.Accounts;

public sealed class AccountPageTests : BunitContext
{
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 14, 12, 0, 0, TimeSpan.Zero));
    private readonly FakeAccountCorpus _fake = new();

    public AccountPageTests()
    {
        Services.AddSingleton<TimeProvider>(_clock);
        Services.AddSingleton<IAccountCorpus>(sp => new CachedAccountCorpus(_fake, sp.GetRequiredService<TimeProvider>()));
    }

    [Fact]
    public void Renders_facts_row_caveat_year_chart_and_article_links()
    {
        _fake.Accounts["Pohjantähti77"] = SampleFacts("Pohjantähti77");

        var cut = Render<AccountPage>(p => p.Add(x => x.Name, "Pohjantähti77"));

        var title = cut.FindComponent<PageTitle>();
        Assert.Contains("Pohjantähti77 · Ampersand of Truth", Render(title.Instance.ChildContent!).Markup);

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
