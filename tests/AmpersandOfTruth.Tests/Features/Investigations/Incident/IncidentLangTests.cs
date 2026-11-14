using AmpersandOfTruth.Tests.Features.Translation;
using AmpersandOfTruth.Web.Features.Evidence;
using AmpersandOfTruth.Web.Features.Investigations.Incident;
using AmpersandOfTruth.Web.Features.Investigations.View;
using AmpersandOfTruth.Web.Features.Translation;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.JSInterop;

namespace AmpersandOfTruth.Tests.Features.Investigations.Incident;

public sealed class IncidentLangTests : BunitContext
{
    private readonly FakeIncidentSource _source = new();
    private readonly FakeEvidenceTranslator _translator = new();
    private readonly LanguagePreference _langPreference = new();

    public IncidentLangTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton<IIncidentSource>(_source);
        Services.AddSingleton<IEvidenceTranslator>(_translator);
        Services.AddSingleton(_langPreference);
        Services.AddSingleton<IOptions<InvestigationSourceOptions>>(
            Options.Create(new InvestigationSourceOptions { Source = IncidentSourceKind.Fixtures }));
        Services.AddLogging();
    }

    [Fact]
    public void Stored_fi_loads_after_first_render_and_passes_Lang_fi_to_DiffViewer_while_keeping_headline_English()
    {
        JSInterop.Setup<string?>("localStorage.getItem", LanguagePreference.StorageKey)
            .SetResult("fi");

        _source.WithTopic("winter-war", FakeIncidentSource.BuildSix());
        var nav = Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo("http://localhost/investigate/winter-war/incidents/ww-02");

        var cut = Render<IncidentPage>(p => p
            .Add(x => x.Topic, "winter-war")
            .Add(x => x.IncidentId, "ww-02"));

        cut.WaitForAssertion(() =>
        {
            var diff = cut.FindComponent<DiffViewer>();
            Assert.Equal("fi", diff.Instance.Lang);
            Assert.Empty(cut.FindAll("[data-testid=diff-mt]"));

            var buttons = cut.FindAll("[data-testid=diff-lang-toggle] button");
            Assert.Equal("false", buttons[0].GetAttribute("aria-pressed"));
            Assert.Equal("true", buttons[1].GetAttribute("aria-pressed"));
        });

        // English-first everywhere else stays default
        Assert.Equal(
            "Finland started the Winter War by shelling the village of Mainila.",
            cut.Find("[data-testid=incident-claim]").TextContent);

        // Moving to the next incident keeps FI even without ?lang in the URL
        nav.NavigateTo("http://localhost/investigate/winter-war/incidents/ww-03");
        cut.Render(p => p
            .Add(x => x.Topic, "winter-war")
            .Add(x => x.IncidentId, "ww-03"));

        Assert.Equal("fi", cut.FindComponent<DiffViewer>().Instance.Lang);
    }

    [Theory]
    [InlineData("fi", "en", "en")]
    [InlineData("en", "fi", "fi")]
    public void Query_lang_overrides_stored_preference_in_both_directions(
        string storedLang,
        string queryLang,
        string expectedLang)
    {
        JSInterop.Setup<string?>("localStorage.getItem", LanguagePreference.StorageKey)
            .SetResult(storedLang);

        _source.WithTopic("winter-war", FakeIncidentSource.BuildSix());
        var nav = Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo($"http://localhost/investigate/winter-war/incidents/ww-02?lang={queryLang}");

        var cut = Render<IncidentPage>(p => p
            .Add(x => x.Topic, "winter-war")
            .Add(x => x.IncidentId, "ww-02"));

        cut.WaitForAssertion(() =>
        {
            var diff = cut.FindComponent<DiffViewer>();
            Assert.Equal(expectedLang, diff.Instance.Lang);

            var buttons = cut.FindAll("[data-testid=diff-lang-toggle] button");
            Assert.Equal(expectedLang == "en" ? "true" : "false", buttons[0].GetAttribute("aria-pressed"));
            Assert.Equal(expectedLang == "fi" ? "true" : "false", buttons[1].GetAttribute("aria-pressed"));
        });

        // Query override does not overwrite localStorage on load
        Assert.DoesNotContain(JSInterop.Invocations, i => i.Identifier == "localStorage.setItem");
    }

    [Fact]
    public void Toggling_lang_in_DiffViewer_saves_preference_to_localStorage_and_updates_DiffViewer()
    {
        JSInterop.Setup<string?>("localStorage.getItem", LanguagePreference.StorageKey)
            .SetResult((string?)null);
        var setHandler = JSInterop.SetupVoid("localStorage.setItem", _ => true);
        setHandler.SetVoidResult();

        _source.WithTopic("winter-war", FakeIncidentSource.BuildSix());
        var nav = Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo("http://localhost/investigate/winter-war/incidents/ww-02");

        var cut = Render<IncidentPage>(p => p
            .Add(x => x.Topic, "winter-war")
            .Add(x => x.IncidentId, "ww-02"));

        Assert.Equal("en", cut.FindComponent<DiffViewer>().Instance.Lang);

        // Toggle EN -> FI
        cut.FindAll("[data-testid=diff-lang-toggle] button")[1].Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("fi", _langPreference.Lang);
            Assert.Equal("fi", cut.FindComponent<DiffViewer>().Instance.Lang);
            Assert.Empty(cut.FindAll("[data-testid=diff-mt]"));
        });

        var fiSave = Assert.Single(JSInterop.Invocations, i => i.Identifier == "localStorage.setItem");
        Assert.Equal([LanguagePreference.StorageKey, "fi"], fiSave.Arguments);
        Assert.Contains("lang=fi", nav.Uri, StringComparison.Ordinal);

        // Toggle FI -> EN
        cut.FindAll("[data-testid=diff-lang-toggle] button")[0].Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("en", _langPreference.Lang);
            Assert.Equal("en", cut.FindComponent<DiffViewer>().Instance.Lang);
            Assert.NotEmpty(cut.FindAll("[data-testid=diff-mt]"));
        });

        var saves = JSInterop.Invocations.Where(i => i.Identifier == "localStorage.setItem").ToList();
        Assert.Equal(2, saves.Count);
        Assert.Equal([LanguagePreference.StorageKey, "en"], saves[1].Arguments);
        Assert.DoesNotContain("lang=", nav.Uri, StringComparison.Ordinal);
    }

    [Fact]
    public void Js_throwing_falls_back_cleanly_to_en_without_crashing()
    {
        JSInterop.Mode = JSRuntimeMode.Strict;
        JSInterop.Setup<string?>("localStorage.getItem", LanguagePreference.StorageKey)
            .SetException(new JSException("localStorage blocked"));
        JSInterop.SetupVoid("Blazor._internal.domWrapper.focus", _ => true)
            .SetException(new JSException("focus unavailable"));

        _source.WithTopic("winter-war", FakeIncidentSource.BuildSix());
        var nav = Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo("http://localhost/investigate/winter-war/incidents/ww-02");

        var cut = Render<IncidentPage>(p => p
            .Add(x => x.Topic, "winter-war")
            .Add(x => x.IncidentId, "ww-02"));

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("en", _langPreference.Lang);
            Assert.Equal("en", cut.FindComponent<DiffViewer>().Instance.Lang);

            var buttons = cut.FindAll("[data-testid=diff-lang-toggle] button");
            Assert.Equal("true", buttons[0].GetAttribute("aria-pressed"));
            Assert.Equal("false", buttons[1].GetAttribute("aria-pressed"));
        });
    }

    [Fact]
    public async Task Prerender_never_invokes_IJSRuntime()
    {
        var spyJs = new SpyJsRuntime();
        var services = new ServiceCollection();
        var source = new FakeIncidentSource().WithTopic("winter-war", FakeIncidentSource.BuildSix());

        services.AddLogging();
        services.AddSingleton<IIncidentSource>(source);
        services.AddSingleton<IEvidenceTranslator>(_translator);
        services.AddScoped<LanguagePreference>();
        services.AddSingleton<IJSRuntime>(spyJs);
        services.AddSingleton<NavigationManager>(new TestNavigationManager("http://localhost/investigate/winter-war/incidents/ww-02"));
        services.AddSingleton<IOptions<InvestigationSourceOptions>>(
            Options.Create(new InvestigationSourceOptions { Source = IncidentSourceKind.Fixtures }));

        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, NullLoggerFactory.Instance);

        var html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var parameters = ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(IncidentPage.Topic)] = "winter-war",
                [nameof(IncidentPage.IncidentId)] = "ww-02",
            });
            var output = await renderer.RenderComponentAsync<IncidentPage>(parameters);
            return output.ToHtmlString();
        });

        Assert.Equal(0, spyJs.Calls);
        Assert.Contains("Finland started the Winter War by shelling the village of Mainila.", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Untitled_articles_batch_translate_once_with_topic_slug_scope_and_render_MtLabel_with_original_while_sibling_has_no_MtLabel()
    {
        _translator
            .Returns("Suomen sisällissodan nimet", "Names of the Finnish Civil War")
            .Returns("Punakaarti", "Red Guards");

        var incident = FakeIncidentSource.BuildIncident(
            id: "cw-05",
            rank: 1,
            articles:
            [
                new ArticleRef(100, "Suomen sisällissota", "Finnish Civil War"),
                new ArticleRef(200, "Suomen sisällissodan nimet", null),
                new ArticleRef(300, "Punakaarti", null),
            ],
            edits: []); // empty edits so only title translation calls TranslateManyAsync

        _source.WithTopic("civil-war-1918", [incident]);
        var nav = Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo("http://localhost/investigate/civil-war-1918/incidents/cw-05");

        var cut = Render<IncidentPage>(p => p
            .Add(x => x.Topic, "civil-war-1918")
            .Add(x => x.IncidentId, "cw-05"));

        cut.WaitForAssertion(() =>
        {
            var articlesRow = cut.Find("[data-testid=incident-articles]");
            var mtLabels = articlesRow.QuerySelectorAll("[data-testid=mt-label]");
            Assert.Equal(2, mtLabels.Length);
        });

        // Exactly ONE batch call for both untitled titles, with scope = topic slug ("civil-war-1918")
        Assert.Equal(1, _translator.TranslateManyCalls);
        var batch = Assert.Single(_translator.BatchRequests);
        Assert.Equal(["Suomen sisällissodan nimet", "Punakaarti"], batch.Texts);
        Assert.Equal("fi", batch.SourceLang);
        Assert.Equal("civil-war-1918", batch.Scope);

        var row = cut.Find("[data-testid=incident-articles]");
        var siblingLink = row.QuerySelector("a[href='/articles/100']");
        Assert.NotNull(siblingLink);
        Assert.Null(siblingLink.GetAttribute("lang"));
        Assert.Equal("Finnish Civil War", siblingLink.TextContent);
        var siblingFi = row.QuerySelector("[data-testid=incident-article-fi]");
        Assert.NotNull(siblingFi);
        Assert.Equal("fi", siblingFi.GetAttribute("lang"));
        Assert.Equal("Suomen sisällissota", siblingFi.TextContent);
        Assert.True(
            row.InnerHtml.IndexOf("Finnish Civil War", StringComparison.Ordinal)
            < row.InnerHtml.IndexOf("Suomen sisällissota", StringComparison.Ordinal),
            "Known English title must precede the Finnish title.");

        Assert.NotNull(row.QuerySelector("a[href='/articles/200'][lang='fi']"));
        Assert.NotNull(row.QuerySelector("a[href='/articles/300'][lang='fi']"));

        var firstMt = cut.FindAll("[data-testid=incident-articles] [data-testid=mt-label]")[0];
        Assert.Contains("Names of the Finnish Civil War", firstMt.TextContent);
        Assert.NotNull(firstMt.QuerySelector(".aot-mt__tag"));
        Assert.Empty(cut.FindAll("[data-testid=incident-articles] [data-testid=mt-original]"));

        cut.FindAll("[data-testid=incident-articles] [data-testid=mt-show-original]")[0].Click();

        var shownOriginal = cut.Find("[data-testid=incident-articles] [data-testid=mt-original]");
        Assert.Equal("fi", shownOriginal.GetAttribute("lang"));
        Assert.Equal("Suomen sisällissodan nimet", shownOriginal.TextContent);

        // Re-rendering (e.g. toggling diff layout) does NOT translate titles a second time
        cut.FindAll("[data-testid=diff-view-toggle] button")[1].Click();
        Assert.Equal(1, _translator.TranslateManyCalls);

        Assert.Empty(CopyRules.FindViolations(cut.Find("[data-testid=incident-page]").TextContent, includeJargon: true));
    }

    [Fact]
    public void Sibling_titled_articles_only_make_zero_title_translation_calls_and_render_no_MtLabel()
    {
        var incident = FakeIncidentSource.BuildIncident(
            id: "ww-02",
            rank: 1,
            articles:
            [
                new ArticleRef(1256, "Talvisota", "Winter War"),
                new ArticleRef(5000, "Simo Häyhä", "Simo Häyhä"),
            ],
            edits: []);

        _source.WithTopic("winter-war", [incident]);

        var cut = Render<IncidentPage>(p => p
            .Add(x => x.Topic, "winter-war")
            .Add(x => x.IncidentId, "ww-02"));

        var row = cut.Find("[data-testid=incident-articles]");
        Assert.Empty(row.QuerySelectorAll("[data-testid=mt-label]"));
        var winterLink = row.QuerySelector("a[href='/articles/1256']");
        Assert.NotNull(winterLink);
        Assert.Equal("Winter War", winterLink.TextContent);
        var fiSubtitle = Assert.Single(row.QuerySelectorAll("[data-testid=incident-article-fi]"));
        Assert.Equal("fi", fiSubtitle.GetAttribute("lang"));
        Assert.Equal("Talvisota", fiSubtitle.TextContent);
        Assert.True(
            row.InnerHtml.IndexOf("Winter War", StringComparison.Ordinal)
            < row.InnerHtml.IndexOf("Talvisota", StringComparison.Ordinal));
        Assert.Equal("Simo Häyhä", row.QuerySelector("a[href='/articles/5000']")?.TextContent);
        Assert.Equal(0, _translator.TranslateManyCalls);
    }

    [Fact]
    public void Translator_failure_for_untitled_article_shows_only_Finnish_title_without_MtLabel()
    {
        _translator.ReturnsFailure("Suomen sisällissodan nimet", "HTTP 503");

        var incident = FakeIncidentSource.BuildIncident(
            id: "cw-05",
            rank: 1,
            articles: [new ArticleRef(200, "Suomen sisällissodan nimet", null)],
            edits: []);

        _source.WithTopic("civil-war-1918", [incident]);

        var cut = Render<IncidentPage>(p => p
            .Add(x => x.Topic, "civil-war-1918")
            .Add(x => x.IncidentId, "cw-05"));

        cut.WaitForAssertion(() =>
        {
            Assert.Equal(1, _translator.TranslateManyCalls);
        });

        var row = cut.Find("[data-testid=incident-articles]");
        Assert.Empty(row.QuerySelectorAll("[data-testid=mt-label]"));
        Assert.Equal("Suomen sisällissodan nimet", row.TextContent.Trim());
        var link = Assert.Single(row.QuerySelectorAll("a"));
        Assert.Equal("/articles/200", link.GetAttribute("href"));
        Assert.Equal("fi", link.GetAttribute("lang"));
    }

    private sealed class SpyJsRuntime : IJSRuntime
    {
        public int Calls { get; private set; }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            Calls++;
            throw new InvalidOperationException($"JS should not be called during prerender: {identifier}");
        }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            Calls++;
            throw new InvalidOperationException($"JS should not be called during prerender: {identifier}");
        }
    }

    private sealed class TestNavigationManager : NavigationManager
    {
        public TestNavigationManager(string uri)
        {
            Initialize("http://localhost/", uri);
        }

        protected override void NavigateToCore(string uri, bool forceLoad)
        {
            Uri = ToAbsoluteUri(uri).ToString();
            NotifyLocationChanged(isInterceptedLink: false);
        }
    }
}
