using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using AmpersandOfTruth.Tests.Features.Investigations;
using AmpersandOfTruth.Tests.Features.Investigations.View;
using AmpersandOfTruth.Tests.Infrastructure;
using AmpersandOfTruth.Web.Features.Analysis;
using AmpersandOfTruth.Web.Features.Evidence;
using AmpersandOfTruth.Web.Features.Investigations;
using AmpersandOfTruth.Web.Features.Investigations.Fixtures;
using AmpersandOfTruth.Web.Features.Investigations.Pipeline;
using AmpersandOfTruth.Web.Features.Investigations.View;
using AmpersandOfTruth.Web.Features.Library;
using AmpersandOfTruth.Web.Features.Reports;
using AmpersandOfTruth.Web.Features.Sharing;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace AmpersandOfTruth.Tests.Features.Sharing;

public sealed class SharedReportPageTests : BunitContext
{
    private readonly TestDb _db = new();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 14, 17, 0, 0, TimeSpan.Zero));
    private readonly FakeIncidentCorpus _incidentCorpus = new();
    private readonly FakeRevisionStore _revisionStore = new();
    private readonly FakeAnalysisModel _llm = new();
    private readonly LibraryStore _libraryStore;
    private readonly ShareLinkStore _shareStore;
    private readonly SampleTopic _winterWar;

    public SharedReportPageTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        var samples = new SampleIncidents(NullLogger<SampleIncidents>.Instance);
        _winterWar = samples.Match("winter-war")
            ?? throw new InvalidOperationException("Expected winter-war sample fixture.");

        _db.AddTo(Services);
        Services.AddSingleton<TimeProvider>(_clock);
        Services.AddSingleton<IIncidentCorpus>(_incidentCorpus);
        Services.AddSingleton<IRevisionStore>(_revisionStore);
        Services.AddSingleton<IncidentMapper>();
        Services.AddSingleton<IAnalysisModel>(_llm);
        Services.AddLibraryFeature();
        Services.AddReportsFeature();
        Services.AddSharingFeature();

        _libraryStore = Services.GetRequiredService<LibraryStore>();
        _shareStore = Services.GetRequiredService<ShareLinkStore>();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _db.Dispose();
        }
    }

    [Fact]
    public async Task Shared_page_renders_read_only_report_with_only_print_button_intro_methodology_levels_attribution_and_anchors()
    {
        _llm.Returns(
            SummarizeInvestigationPrompt.Id,
            """{"summary":"Shared report automated summary text."}""");

        var incidents = _winterWar.Incidents.Take(2).ToList();
        var analysedAt = new DateTimeOffset(2026, 11, 14, 15, 0, 0, TimeSpan.Zero);
        var snapshot = new SavedSnapshot(
            _winterWar.Title,
            [new TopicArticle("fi", 1256, "Talvisota", 0.04)],
            incidents,
            analysedAt);

        var savedId = await _libraryStore.SaveAsync("owner-share", "Winter War Shared Dossier", snapshot);
        // Pin only the first incident: shared report must still render ALL incidents (PinnedOnly: false).
        await _libraryStore.PinAsync(savedId, "owner-share", incidents[0].Id!, "Verify archive reference.");

        var token = await _shareStore.CreateAsync(savedId, "owner-share", lifetime: null);
        Assert.NotNull(token);

        var cut = Render<SharedReportPage>(p => p.Add(x => x.Token, token));

        cut.WaitForAssertion(() =>
        {
            var sharedReport = cut.Find("[data-testid=shared-report]");
            Assert.Contains("Winter War Shared Dossier", sharedReport.TextContent);

            var intro = cut.Find("[data-testid=shared-intro]");
            Assert.Contains(
                "Shared report from Ampersand of Truth — read-only snapshot analysed 14 Nov 2026",
                intro.TextContent);
            Assert.NotNull(intro.QuerySelector($"a[href='{Attribution.MethodologyRoute}']"));

            // All 2 incidents shown (PinnedOnly: false), with #incident-{IncidentId} anchors
            var renderedIncidents = cut.FindAll("[data-testid=report-incident]");
            Assert.Equal(2, renderedIncidents.Count);
            Assert.Equal($"incident-{incidents[0].Id}", renderedIncidents[0].GetAttribute("id"));
            Assert.Equal($"incident-{incidents[1].Id}", renderedIncidents[1].GetAttribute("id"));

            // Read-only: NO buttons anywhere except print
            var buttons = cut.FindAll("button");
            var soleButton = Assert.Single(buttons);
            Assert.Equal("report-print", soleButton.GetAttribute("data-testid"));

            Assert.Empty(cut.FindAll("[data-testid=report-pinned-toggle]"));
            Assert.Empty(cut.FindAll("[data-testid=report-download-md]"));
            Assert.Empty(cut.FindAll("[data-testid=share-button]"));
            Assert.Empty(cut.FindAll("[data-testid=rerun-latest]"));

            // Owner's SavedId capability GUID must not leak anywhere in markup
            Assert.DoesNotContain(savedId.ToString(), cut.Markup, StringComparison.OrdinalIgnoreCase);

            // Appendix includes methodology link, EvidenceTiers level explanations, and attribution
            var appendix = cut.Find("[data-testid=report-appendix]");
            Assert.NotNull(appendix.QuerySelector($"a[href='{Attribution.MethodologyRoute}']"));
            Assert.Contains(Methodology.Summary, appendix.TextContent);
            Assert.Contains(EvidenceTiers.Campaign.Meaning, appendix.TextContent);
            Assert.Contains(EvidenceTiers.Contested.Meaning, appendix.TextContent);
            Assert.Contains(EvidenceTiers.Reverted.Meaning, appendix.TextContent);
            Assert.Contains(Attribution.FooterText, appendix.TextContent);
            Assert.Contains(Attribution.License, appendix.TextContent);
        });

        // Print button triggers window.print()
        cut.Find("[data-testid=report-print]").Click();
        Assert.Contains(JSInterop.Invocations, inv => inv.Identifier == "print");

        // Rendered page copy passes CopyRules
        var doc = new HtmlParser().ParseDocument(cut.Markup);
        var visibleText = string.Join(" ", doc.Descendants<IText>().Select(t => t.Text).Where(t => !string.IsNullOrWhiteSpace(t)));
        Assert.Empty(CopyRules.FindViolations(visibleText, includeJargon: false));
    }

    [Fact]
    public async Task Unknown_malformed_revoked_and_expired_tokens_render_identical_generic_not_found_state()
    {
        var snapshot = new SavedSnapshot(
            _winterWar.Title,
            [new TopicArticle("fi", 1256, "Talvisota", 0.04)],
            _winterWar.Incidents.Take(1).ToList(),
            _clock.GetUtcNow());

        var savedId = await _libraryStore.SaveAsync("owner-1", "Confidential Topic", snapshot);
        var revocableToken = (await _shareStore.CreateAsync(savedId, "owner-1", lifetime: null))!;
        var expiringToken = (await _shareStore.CreateAsync(savedId, "owner-1", TimeSpan.FromDays(7)))!;

        var revocablePrefix = ShareLinkStore.HashToken(revocableToken)[..8];
        Assert.True(await _shareStore.RevokeAsync(savedId, "owner-1", revocablePrefix));
        _clock.Advance(TimeSpan.FromDays(8));

        var invalidTokens = new[]
        {
            "doesnotexist",
            ShareLinkStore.GenerateToken(),
            new string('B', 43),
            revocableToken,
            expiringToken,
        };

        foreach (var badToken in invalidTokens)
        {
            var cut = Render<SharedReportPage>(p => p.Add(x => x.Token, badToken));
            cut.WaitForAssertion(() =>
            {
                var notFound = cut.Find("[data-testid=shared-not-found]");
                Assert.Contains("This link is not valid or has been revoked.", notFound.TextContent);
                Assert.DoesNotContain("Confidential Topic", cut.Markup, StringComparison.OrdinalIgnoreCase);
                Assert.Empty(cut.FindAll("[data-testid=shared-report]"));
            });
        }
    }

    [Fact]
    public async Task Share_button_creates_one_time_url_lists_hash_prefix_copies_and_revokes()
    {
        var owner = Services.GetRequiredService<OwnerKey>();
        owner.Seed("owner-ui");

        var snapshot = new SavedSnapshot(
            _winterWar.Title,
            [new TopicArticle("fi", 1256, "Talvisota", 0.04)],
            _winterWar.Incidents.Take(1).ToList(),
            _clock.GetUtcNow());

        var savedId = await _libraryStore.SaveAsync("owner-ui", "Shareable Dossier", snapshot);

        var libCut = Render<LibraryPage>();
        libCut.WaitForAssertion(() => Assert.NotNull(libCut.Find("[data-testid=share-button]")));

        // Also check SavedInvestigationBanner renders share-button
        var nav = Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo($"/investigate/winter-war?saved={savedId}");
        var bannerCut = Render<SavedInvestigationBanner>(p => p.Add(x => x.AnalysedAt, _clock.GetUtcNow()));
        Assert.NotNull(bannerCut.Find("[data-testid=share-button]"));

        // Open share panel on LibraryPage
        libCut.Find("[data-testid=share-button]").Click();
        libCut.WaitForAssertion(() =>
        {
            Assert.NotNull(libCut.Find("[data-testid=share-panel]"));
            Assert.NotNull(libCut.Find("[data-testid=share-expiry]"));
            Assert.NotNull(libCut.Find("[data-testid=share-create]"));
            Assert.Contains("No active share links.", libCut.Find("[data-testid=share-links]").TextContent);
        });

        // Select 7 days expiry and create link
        libCut.Find("[data-testid=share-expiry]").Change("7d");
        libCut.Find("[data-testid=share-create]").Click();

        string? createdUrl = null;
        string? rawToken = null;
        libCut.WaitForAssertion(() =>
        {
            createdUrl = libCut.Find("[data-testid=share-url]").GetAttribute("value");
            Assert.NotNull(createdUrl);
            Assert.Contains("/r/", createdUrl);

            rawToken = createdUrl.Split("/r/", StringSplitOptions.None)[1];
            Assert.Equal(43, rawToken.Length);

            var expectedPrefix = ShareLinkStore.HashToken(rawToken)[..8];
            var linksText = libCut.Find("[data-testid=share-links]").TextContent;
            Assert.Contains(expectedPrefix, linksText);
            Assert.Contains("14 Nov 2026", linksText);
            Assert.DoesNotContain(rawToken, linksText, StringComparison.Ordinal);
            Assert.Single(libCut.FindAll("[data-testid=share-revoke]"));
        });

        Assert.Equal(savedId, await _shareStore.ResolveAsync(rawToken!));

        // Copy button writes URL to clipboard
        libCut.Find("[data-testid=share-copy]").Click();
        Assert.Contains(
            JSInterop.Invocations,
            inv => inv.Identifier == "navigator.clipboard.writeText"
                && inv.Arguments.Count == 1
                && string.Equals(inv.Arguments[0]?.ToString(), createdUrl, StringComparison.Ordinal));

        // Closing and reopening share-panel hides the raw URL (shown once), while hash prefix remains listed
        libCut.Find("[data-testid=share-button]").Click();
        libCut.WaitForAssertion(() => Assert.Empty(libCut.FindAll("[data-testid=share-panel]")));

        libCut.Find("[data-testid=share-button]").Click();
        libCut.WaitForAssertion(() =>
        {
            Assert.Empty(libCut.FindAll("[data-testid=share-url]"));
            Assert.Single(libCut.FindAll("[data-testid=share-revoke]"));
        });

        // Revoke removes link and invalidates token
        libCut.Find("[data-testid=share-revoke]").Click();
        libCut.WaitForAssertion(() => Assert.Empty(libCut.FindAll("[data-testid=share-revoke]")));
        Assert.Null(await _shareStore.ResolveAsync(rawToken!));
    }
}
