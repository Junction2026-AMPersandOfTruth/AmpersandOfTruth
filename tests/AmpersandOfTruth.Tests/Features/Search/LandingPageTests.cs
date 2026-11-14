using AmpersandOfTruth.Web.Features.Evidence;
using AmpersandOfTruth.Web.Features.Investigations.View;
using AmpersandOfTruth.Web.Features.Search;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace AmpersandOfTruth.Tests.Features.Search;

public sealed class LandingPageTests : BunitContext
{
    public LandingPageTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddHealthChecks();
        Services.AddSearchFeature();
    }

    [Fact]
    public void Out_of_scope_topic_with_close_matches_shows_nearest_prompt_and_resolves_configured_chip_slugs()
    {
        Services.Configure<SearchOptions>(o =>
        {
            o.SuggestedTopics =
            [
                new("1918 Civil War", "Civil War 1918"),
                new("Winter War", "Winter War"),
                new("Finlandization", "Finlandization"),
            ];
        });
        Services.AddSingleton<IScopeCheck>(
            new FakeScopeCheck(false, ["1918 Civil War", "Winter War"]));

        var nav = Services.GetRequiredService<NavigationManager>();
        var cut = Render<LandingPage>();

        cut.Find("[data-testid=topic-input]").Change("American Civil War");
        cut.Find("form").Submit();

        cut.WaitForAssertion(() =>
        {
            var outOfScope = cut.Find("[data-testid=out-of-scope]");
            Assert.Contains("Our data covers the history of Finland. ‘American Civil War’ is outside it.", outOfScope.TextContent);
            Assert.Contains("Try one of the nearest topics in our dataset:", outOfScope.TextContent);
            Assert.Empty(CopyRules.FindViolations(outOfScope.TextContent, includeJargon: true));

            var chips = outOfScope.QuerySelectorAll("a.aot-chip");
            Assert.Equal(2, chips.Length);
            Assert.Equal("/investigate/civil-war-1918", chips[0].GetAttribute("href"));
            Assert.Equal("1918 Civil War", chips[0].TextContent.Trim());
            Assert.Equal("/investigate/winter-war", chips[1].GetAttribute("href"));
            Assert.Equal("Winter War", chips[1].TextContent.Trim());
        });

        Assert.Equal("http://localhost/", nav.Uri);
    }

    [Fact]
    public void Out_of_scope_topic_when_nothing_is_close_shows_finnish_history_prompt_and_configured_chips()
    {
        Services.AddSingleton<IScopeCheck>(
            new FakeScopeCheck(false, []));

        var nav = Services.GetRequiredService<NavigationManager>();
        var cut = Render<LandingPage>();

        cut.Find("[data-testid=topic-input]").Change("climate change");
        cut.Find("form").Submit();

        cut.WaitForAssertion(() =>
        {
            var outOfScope = cut.Find("[data-testid=out-of-scope]");
            Assert.Contains("Our data covers the history of Finland. ‘climate change’ is outside it.", outOfScope.TextContent);
            Assert.Contains("Try a topic from the history of Finland:", outOfScope.TextContent);
            Assert.DoesNotContain("nearest topics", outOfScope.TextContent, StringComparison.OrdinalIgnoreCase);
            Assert.Empty(CopyRules.FindViolations(outOfScope.TextContent, includeJargon: true));

            var chips = outOfScope.QuerySelectorAll("[data-testid=suggestion-chip]");
            Assert.Equal(SearchOptions.DefaultSuggestedTopics.Count, chips.Length);
            Assert.Equal("Civil War 1918", chips[0].TextContent.Trim());
            Assert.Equal("/investigate/civil-war-1918", chips[0].GetAttribute("href"));
        });

        Assert.Equal("http://localhost/", nav.Uri);
    }

    [Fact]
    public void Recent_items_from_localStorage_render_on_landing_page()
    {
        JSInterop.Setup<string?>("localStorage.getItem", RecentTopics.StorageKey)
            .SetResult("""[{"label":"Winter War","slug":"winter-war"},{"label":"Ceded Karelia","slug":"ceded-karelia"}]""");

        var cut = Render<LandingPage>();

        cut.WaitForAssertion(() =>
        {
            var items = cut.FindAll("[data-testid=recent-item]");
            Assert.Equal(2, items.Count);
            Assert.Equal("Winter War", items[0].TextContent.Trim());
            Assert.Equal("/investigate/winter-war", items[0].GetAttribute("href"));
            Assert.Equal("Ceded Karelia", items[1].TextContent.Trim());
            Assert.Equal("/investigate/ceded-karelia", items[1].GetAttribute("href"));
        });
    }

    [Fact]
    public void Recent_when_js_throws_hides_section_without_exception()
    {
        JSInterop.Setup<string?>("localStorage.getItem", RecentTopics.StorageKey)
            .SetException(new InvalidOperationException("localStorage blocked"));

        var cut = Render<LandingPage>();

        Assert.Empty(cut.FindAll("[data-testid=recent-item]"));
        Assert.DoesNotContain("Recent in this browser", cut.Markup);
    }

    [Fact]
    public void Degraded_corpus_or_embedding_health_shows_warn_status_banner_with_detail()
    {
        Services.AddHealthChecks()
            .AddCheck("corpus", () => HealthCheckResult.Degraded("corpus 127.0.0.1:3307 unreachable"), ["corpus"])
            .AddCheck("embedding", () => HealthCheckResult.Healthy("bge-m3 ok"), ["embedding"]);

        var cut = Render<LandingPage>();

        cut.WaitForAssertion(() =>
        {
            var banner = cut.Find("[data-testid=status-banner]");
            Assert.Contains("aot-banner--warn", banner.ClassList);
            Assert.Contains("Search is limited right now", banner.TextContent);
            Assert.Contains("chips still open saved investigations", banner.TextContent);
            Assert.Contains("corpus 127.0.0.1:3307 unreachable", banner.TextContent);
        });
    }

    [Fact]
    public void Rendered_landing_copy_has_no_copy_or_jargon_violations_and_default_slugs_match_fixtures()
    {
        var cut = Render<LandingPage>();

        var visibleText = cut.Nodes.OfType<AngleSharp.Dom.IElement>()
            .Select(e => e.TextContent)
            .Aggregate("", (a, b) => a + " " + b);

        Assert.Empty(CopyRules.FindViolations(visibleText, includeJargon: true));

        var defaultSlugs = SearchOptions.DefaultSuggestedTopics.Select(t => TopicSlug.From(t.Topic)).ToList();
        Assert.Equal(
            ["civil-war-1918", "winter-war", "continuation-war", "mannerheim", "mainila-shots", "ceded-karelia", "finlandization"],
            defaultSlugs);
    }

    [Fact]
    public void State_landing_loading()
    {
        var cut = Render<LandingPage>();

        Assert.NotNull(cut.Find("[data-testid=topic-input]"));
        Assert.NotNull(cut.Find("[data-testid=topic-submit]"));
        Assert.NotEmpty(cut.FindAll("[data-testid=topic-chip]"));
        Assert.Empty(cut.FindAll("[data-testid=status-banner]"));
    }

    [Fact]
    public void State_landing_empty()
    {
        Services.AddSingleton<IScopeCheck>(
            new FakeScopeCheck(false, ["Winter War", "Civil War 1918", "Mannerheim"]));

        var cut = Render<LandingPage>();
        Assert.Empty(cut.FindAll("[data-testid=recent-topics]"));

        cut.Find("[data-testid=topic-input]").Change("climate change");
        cut.Find("form").Submit();

        cut.WaitForAssertion(() =>
        {
            var empty = cut.Find("[data-testid=out-of-scope] [data-testid=empty-state]");
            Assert.Contains("climate change", empty.TextContent);
            Assert.Equal(3, cut.FindAll("[data-testid=suggestion-chip]").Count);
        });
    }

    [Fact]
    public void State_landing_corpus_down()
    {
        Services.AddHealthChecks()
            .AddCheck("corpus", () => HealthCheckResult.Unhealthy("corpus 127.0.0.1:1 unreachable"), ["corpus"])
            .AddCheck("embedding", () => HealthCheckResult.Healthy("bge-m3 ok"), ["embedding"]);

        var nav = Services.GetRequiredService<NavigationManager>();
        var cut = Render<LandingPage>();

        cut.WaitForAssertion(() =>
        {
            var banner = cut.Find("[data-testid=status-banner]");
            Assert.Contains("aot-banner--error", banner.ClassList);
            Assert.True(cut.Find("[data-testid=topic-input]").HasAttribute("disabled"));
            Assert.True(cut.Find("[data-testid=topic-submit]").HasAttribute("disabled"));

            var chips = cut.FindAll("[data-testid=topic-chip]");
            Assert.NotEmpty(chips);
            Assert.StartsWith("/investigate/", chips[0].GetAttribute("href"));
        });

        cut.Find("form").Submit();
        Assert.Equal("http://localhost/", nav.Uri);
    }

    [Fact]
    public void State_landing_embedder_down()
    {
        Services.AddHealthChecks()
            .AddCheck("corpus", () => HealthCheckResult.Healthy("corpus ok"), ["corpus"])
            .AddCheck("embedding", () => HealthCheckResult.Degraded("no Ollama with 'bge-m3'"), ["embedding"]);

        var cut = Render<LandingPage>();

        cut.WaitForAssertion(() =>
        {
            var banner = cut.Find("[data-testid=status-banner]");
            Assert.Contains("Search is limited right now", banner.TextContent);
            Assert.Contains("chips still open saved investigations", banner.TextContent);
            Assert.True(cut.Find("[data-testid=topic-input]").HasAttribute("disabled"));
            Assert.True(cut.Find("[data-testid=topic-submit]").HasAttribute("disabled"));
            Assert.NotEmpty(cut.FindAll("[data-testid=topic-chip]"));
        });
    }

    [Fact]
    public void State_landing_llm_down()
    {
        Services.AddHealthChecks()
            .AddCheck("corpus", () => HealthCheckResult.Healthy("corpus ok"), ["corpus"])
            .AddCheck("embedding", () => HealthCheckResult.Healthy("bge-m3 ok"), ["embedding"])
            .AddCheck("gemini", () => HealthCheckResult.Degraded("no api key"), ["gemini"]);

        var cut = Render<LandingPage>();

        Assert.Empty(cut.FindAll("[data-testid=status-banner]"));
        Assert.False(cut.Find("[data-testid=topic-input]").HasAttribute("disabled"));
        Assert.False(cut.Find("[data-testid=topic-submit]").HasAttribute("disabled"));
    }

    [Fact]
    public void State_landing_slow()
    {
        var pending = new TaskCompletionSource<HealthCheckResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        Services.AddHealthChecks()
            .AddAsyncCheck("corpus", _ => pending.Task, ["corpus"]);

        var cut = Render<LandingPage>();

        Assert.NotNull(cut.Find("[data-testid=topic-input]"));
        Assert.NotEmpty(cut.FindAll("[data-testid=topic-chip]"));
        Assert.Empty(cut.FindAll("[data-testid=status-banner]"));
    }

    [Fact]
    public void State_landing_run_failed()
    {
        Services.AddSingleton<IScopeCheck>(new ThrowingScopeCheck());
        var nav = Services.GetRequiredService<NavigationManager>();
        var cut = Render<LandingPage>();

        cut.Find("[data-testid=topic-input]").Change("Winter War");
        cut.Find("form").Submit();

        cut.WaitForAssertion(() =>
            Assert.Equal("http://localhost/investigate/winter-war", nav.Uri));
    }

    private sealed class FakeScopeCheck(bool inScope, IReadOnlyList<string> suggestions) : IScopeCheck
    {
        public Task<ScopeResult> CheckAsync(string topic, CancellationToken ct = default) =>
            Task.FromResult(new ScopeResult(inScope, suggestions));
    }

    private sealed class ThrowingScopeCheck : IScopeCheck
    {
        public Task<ScopeResult> CheckAsync(string topic, CancellationToken ct = default) =>
            Task.FromException<ScopeResult>(new InvalidOperationException("scope check failed"));
    }
}
