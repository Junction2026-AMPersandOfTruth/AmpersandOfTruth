using AmpersandOfTruth.Tests.Features.Accounts;
using AmpersandOfTruth.Tests.Features.Translation;
using AmpersandOfTruth.Web.Features.Accounts;
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
using Microsoft.Extensions.Time.Testing;

namespace AmpersandOfTruth.Tests.Features.Investigations.Incident;

public sealed class IncidentAccountsTests : BunitContext
{
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 14, 12, 0, 0, TimeSpan.Zero));
    private readonly FakeAccountCorpus _fakeCorpus = new();

    public IncidentAccountsTests()
    {
        Services.AddSingleton<TimeProvider>(_clock);
        Services.AddSingleton<IAccountCorpus>(sp => new CachedAccountCorpus(_fakeCorpus, sp.GetRequiredService<TimeProvider>()));
    }

    [Fact]
    public void Groups_added_and_removed_distinct_by_GroupingKey_and_places_editor_who_both_added_and_removed_in_both_groups()
    {
        _fakeCorpus.Accounts["Pohjantähti 77"] = new AccountFacts(
            User: "Pohjantähti 77",
            Edits: 1_204,
            Articles: 37,
            RemovedEdits: 112,
            First: new DateTime(2009, 3, 3, 14, 2, 0, DateTimeKind.Utc),
            Last: new DateTime(2024, 2, 2, 9, 15, 0, DateTimeKind.Utc),
            PerYear: [],
            TopArticles: []);
        _fakeCorpus.Accounts["Kumoaja"] = new AccountFacts(
            User: "Kumoaja",
            Edits: 510,
            Articles: 19,
            RemovedEdits: 4,
            First: null,
            Last: null,
            PerYear: [],
            TopArticles: []);

        var t0 = new DateTimeOffset(2014, 3, 3, 14, 2, 0, TimeSpan.Zero);
        var incident = FakeIncidentSource.BuildIncident(
            edits:
            [
                // Pohjantähti 77 inserts once and reinserts once (2 in "Added it"), and also removes once (1 in "Removed it")
                new EditView(1001, 1000, 1256, t0, EditorDisplay.For("Pohjantähti 77"), null, EditAction.Inserted, TimeSpan.FromDays(1)),
                new EditView(1002, 1001, 1256, t0.AddDays(1), EditorDisplay.For("Kumoaja"), null, EditAction.Removed, null),
                new EditView(1003, 1002, 1256, t0.AddDays(2), EditorDisplay.For("Pohjantähti 77"), null, EditAction.Reinserted, TimeSpan.FromDays(1)),
                // Two different IPs in the same /16 masked range group into 1 row ("2 edits here")
                new EditView(1004, 1003, 1256, t0.AddDays(3), EditorDisplay.For("85.76.10.1"), null, EditAction.Reinserted, TimeSpan.FromDays(1)),
                new EditView(1005, 1004, 1256, t0.AddDays(4), EditorDisplay.For("85.76.200.99"), null, EditAction.Reinserted, TimeSpan.FromDays(1)),
                new EditView(1006, 1005, 1256, t0.AddDays(5), EditorDisplay.For("Pohjantähti 77"), null, EditAction.Removed, null),
            ]);

        var cut = Render<IncidentAccounts>(p => p.Add(x => x.Incident, incident));

        cut.WaitForAssertion(() =>
        {
            Assert.NotEmpty(cut.FindAll("[data-testid=account-facts-line]"));
        });

        var panel = cut.Find("[data-testid=incident-accounts]");
        Assert.Equal("Accounts involved", panel.QuerySelector("h3")?.TextContent);

        var addedRows = cut.FindAll("[data-testid=accounts-added] [data-testid=account-row]");
        Assert.Equal(2, addedRows.Count);

        // Added row 0: Pohjantähti 77 (2 edits here + escaped account page link + facts line)
        Assert.Contains("Pohjantähti 77", addedRows[0].TextContent);
        Assert.Contains("2 edits here", addedRows[0].TextContent);
        var addedLink = addedRows[0].QuerySelector("[data-testid=account-link]");
        Assert.NotNull(addedLink);
        Assert.Equal("Account page", addedLink.TextContent);
        Assert.Equal("/accounts/Pohjant%C3%A4hti%2077", addedLink.GetAttribute("href"));
        Assert.Equal(
            "1,204 edits in our dataset \u00B7 37 articles \u00B7 112 later removed",
            addedRows[0].QuerySelector("[data-testid=account-facts-line]")?.TextContent);

        // Added row 1: 85.76.x.x (2 edits here from merged /16 range, masked, no link, no facts)
        Assert.Contains("85.76.x.x", addedRows[1].TextContent);
        Assert.Contains("anonymous", addedRows[1].TextContent);
        Assert.Contains("2 edits here", addedRows[1].TextContent);
        Assert.DoesNotContain("85.76.10.1", addedRows[1].InnerHtml);
        Assert.DoesNotContain("85.76.200.99", addedRows[1].InnerHtml);
        Assert.Empty(addedRows[1].QuerySelectorAll("a"));
        Assert.Null(addedRows[1].QuerySelector("[data-testid=account-facts-line]"));

        var removedRows = cut.FindAll("[data-testid=accounts-removed] [data-testid=account-row]");
        Assert.Equal(2, removedRows.Count);

        // Removed row 0: Kumoaja (1 edit here)
        Assert.Contains("Kumoaja", removedRows[0].TextContent);
        Assert.Contains("1 edit here", removedRows[0].TextContent);
        Assert.Equal("/accounts/Kumoaja", removedRows[0].QuerySelector("[data-testid=account-link]")?.GetAttribute("href"));
        Assert.Equal(
            "510 edits in our dataset \u00B7 19 articles \u00B7 4 later removed",
            removedRows[0].QuerySelector("[data-testid=account-facts-line]")?.TextContent);

        // Removed row 1: Pohjantähti 77 also appears in Removed it (1 edit here)
        Assert.Contains("Pohjantähti 77", removedRows[1].TextContent);
        Assert.Contains("1 edit here", removedRows[1].TextContent);
        Assert.Equal("/accounts/Pohjant%C3%A4hti%2077", removedRows[1].QuerySelector("[data-testid=account-link]")?.GetAttribute("href"));
        Assert.Equal(
            "1,204 edits in our dataset \u00B7 37 articles \u00B7 112 later removed",
            removedRows[1].QuerySelector("[data-testid=account-facts-line]")?.TextContent);

        // Pohjantähti 77 was queried only once despite appearing in both groups, and no IP was ever queried
        Assert.Equal(2, _fakeCorpus.Calls);
        Assert.Contains("Pohjantähti 77", _fakeCorpus.QueriedUsers);
        Assert.Contains("Kumoaja", _fakeCorpus.QueriedUsers);

        Assert.Empty(CopyRules.FindViolations(panel.TextContent, includeJargon: true));
    }

    [Fact]
    public void Anonymous_ip_and_temporary_rows_have_no_links_no_facts_and_never_call_account_corpus()
    {
        var t0 = new DateTimeOffset(2025, 10, 1, 12, 0, 0, TimeSpan.Zero);
        var incident = FakeIncidentSource.BuildIncident(
            edits:
            [
                new EditView(1, 0, 1256, t0, EditorDisplay.For("85.76.123.45"), null, EditAction.Inserted, TimeSpan.FromDays(1)),
                new EditView(2, 1, 1256, t0.AddHours(2), EditorDisplay.For("2001:14bb:51:d813::1"), null, EditAction.Reinserted, TimeSpan.FromDays(1)),
                new EditView(3, 2, 1256, t0.AddHours(4), EditorDisplay.For("~2025-26183-73"), null, EditAction.Removed, null),
            ]);

        var cut = Render<IncidentAccounts>(p => p.Add(x => x.Incident, incident));

        var rows = cut.FindAll("[data-testid=account-row]");
        Assert.Equal(3, rows.Count);

        Assert.Contains("85.76.x.x", rows[0].TextContent);
        Assert.Contains("anonymous", rows[0].TextContent);
        Assert.DoesNotContain("85.76.123.45", cut.Markup);

        Assert.Contains("2001:14BB:\u2026", rows[1].TextContent);
        Assert.Contains("anonymous", rows[1].TextContent);
        Assert.DoesNotContain("d813", cut.Markup, StringComparison.OrdinalIgnoreCase);

        Assert.Contains("~2025-26183-73", rows[2].TextContent);
        Assert.Contains("temporary account", rows[2].TextContent);

        Assert.All(rows, row =>
        {
            Assert.Empty(row.QuerySelectorAll("a"));
            Assert.Null(row.QuerySelector("[data-testid=account-facts-line]"));
        });
        Assert.Equal(0, _fakeCorpus.Calls);
        Assert.Empty(_fakeCorpus.QueriedUsers);
    }

    [Fact]
    public void Facts_load_failure_omits_facts_line_without_throwing_and_keeps_account_link()
    {
        _fakeCorpus.Unavailable = true;
        var incident = FakeIncidentSource.BuildIncident();

        var cut = Render<IncidentAccounts>(p => p.Add(x => x.Incident, incident));

        cut.WaitForAssertion(() =>
        {
            Assert.True(_fakeCorpus.Calls > 0);
        });

        Assert.Empty(cut.FindAll("[data-testid=account-facts-line]"));
        var links = cut.FindAll("[data-testid=account-link]");
        Assert.Equal(2, links.Count);
        Assert.Equal("/accounts/Pohjant%C3%A4hti77", links[0].GetAttribute("href"));
        Assert.Equal("/accounts/Kumoaja", links[1].GetAttribute("href"));
    }

    [Fact]
    public async Task Prerender_does_not_call_account_corpus_and_IncidentPage_places_panel_between_why_and_assessment()
    {
        var source = new FakeIncidentSource().WithTopic("winter-war", FakeIncidentSource.BuildSix());
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IIncidentSource>(source);
        services.AddSingleton<IEvidenceTranslator>(new FakeEvidenceTranslator());
        services.AddSingleton<IAccountCorpus>(_fakeCorpus);
        services.AddSingleton<IOptions<InvestigationSourceOptions>>(
            Options.Create(new InvestigationSourceOptions { Source = IncidentSourceKind.Fixtures }));

        await using (var provider = services.BuildServiceProvider())
        await using (var renderer = new HtmlRenderer(provider, NullLoggerFactory.Instance))
        {
            await renderer.Dispatcher.InvokeAsync(async () =>
            {
                var parameters = ParameterView.FromDictionary(new Dictionary<string, object?>
                {
                    [nameof(IncidentAccounts.Incident)] = FakeIncidentSource.BuildIncident(),
                });
                var output = await renderer.RenderComponentAsync<IncidentAccounts>(parameters);
                Assert.Contains("Accounts involved", output.ToHtmlString(), StringComparison.Ordinal);
            });
        }

        Assert.Equal(0, _fakeCorpus.Calls);

        // Verify side-column order on IncidentPage: WhyFlagged -> IncidentAccounts -> AssessmentNote
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton<IIncidentSource>(source);
        Services.AddSingleton<IEvidenceTranslator>(new FakeEvidenceTranslator());
        Services.AddSingleton<IOptions<InvestigationSourceOptions>>(
            Options.Create(new InvestigationSourceOptions { Source = IncidentSourceKind.Fixtures }));

        var pageCut = Render<IncidentPage>(p => p
            .Add(x => x.Topic, "winter-war")
            .Add(x => x.IncidentId, "ww-02"));

        var sideChildren = pageCut.Find(".aot-incident-side").Children
            .Select(static el => el.GetAttribute("data-testid"))
            .ToArray();

        Assert.Contains("incident-accounts", sideChildren);
        var whyIdx = Array.IndexOf(sideChildren, "why-flagged");
        var accountsIdx = Array.IndexOf(sideChildren, "incident-accounts");
        var assessIdx = Array.IndexOf(sideChildren, "assessment");
        Assert.True(whyIdx >= 0 && whyIdx < accountsIdx && accountsIdx < assessIdx,
            $"Expected why-flagged < incident-accounts < assessment, got [{string.Join(", ", sideChildren)}]");
    }
}
