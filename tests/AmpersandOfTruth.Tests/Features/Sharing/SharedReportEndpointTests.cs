using System.Net;
using AngleSharp.Html.Parser;
using AmpersandOfTruth.Tests.Features.Investigations;
using AmpersandOfTruth.Tests.Features.Investigations.View;
using AmpersandOfTruth.Tests.Infrastructure;
using AmpersandOfTruth.Web.Features.Analysis;
using AmpersandOfTruth.Web.Features.Investigations;
using AmpersandOfTruth.Web.Features.Investigations.Fixtures;
using AmpersandOfTruth.Web.Features.Investigations.Pipeline;
using AmpersandOfTruth.Web.Features.Investigations.View;
using AmpersandOfTruth.Web.Features.Library;
using AmpersandOfTruth.Web.Features.Reports;
using AmpersandOfTruth.Web.Features.Sharing;
using Microsoft.Extensions.DependencyInjection;

namespace AmpersandOfTruth.Tests.Features.Sharing;

public sealed class SharedReportEndpointTests
{
    [Fact]
    public async Task Valid_token_returns_200_with_noindex_generic_og_tags_and_shared_report_while_junk_renders_not_found()
    {
        var fakeLlm = new FakeAnalysisModel().Returns(
            SummarizeInvestigationPrompt.Id,
            """{"summary":"Endpoint shared report automated summary."}""");

        await using var baseFactory = new AppFactory();
        await using var customFactory = baseFactory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("hostBuilder:reloadConfigOnChange", "false");
            builder.UseSetting("Investigations:Fixtures:Warmup", "false");
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IAnalysisModel>(fakeLlm);
                services.AddSingleton<IIncidentCorpus>(new FakeIncidentCorpus { Unavailable = true });
                services.AddSingleton<IRevisionStore>(new FakeRevisionStore { Unavailable = true });
            });
        });

        var libraryStore = customFactory.Services.GetRequiredService<LibraryStore>();
        var shareStore = customFactory.Services.GetRequiredService<ShareLinkStore>();
        var samples = customFactory.Services.GetRequiredService<SampleIncidents>();
        var winterWar = samples.Match("winter-war")
            ?? throw new InvalidOperationException("Expected winter-war sample fixture.");

        var snapshot = new SavedSnapshot(
            winterWar.Title,
            [new TopicArticle("fi", 1256, "Talvisota", 0.04)],
            winterWar.Incidents.Take(2).ToList(),
            new DateTimeOffset(2026, 11, 14, 10, 0, 0, TimeSpan.Zero));

        var savedId = await libraryStore.SaveAsync("endpoint-owner", "Sensitive Topic Investigation", snapshot);
        var token = await shareStore.CreateAsync(savedId, "endpoint-owner", lifetime: null);
        Assert.NotNull(token);

        var client = customFactory.CreateClient();

        // 1. Valid token -> 200 OK, noindex meta, generic Open Graph metadata, shared report content
        var validResponse = await client.GetAsync($"/r/{token}");
        Assert.Equal(HttpStatusCode.OK, validResponse.StatusCode);

        var validHtml = await validResponse.Content.ReadAsStringAsync();
        var doc = new HtmlParser().ParseDocument(validHtml);

        var robotsMeta = doc.QuerySelector("head meta[name='robots']");
        Assert.NotNull(robotsMeta);
        Assert.Equal("noindex, nofollow", robotsMeta.GetAttribute("content"));

        var ogTitle = doc.QuerySelector("head meta[property='og:title']");
        Assert.NotNull(ogTitle);
        Assert.Equal("Shared report · Ampersand of Truth", ogTitle.GetAttribute("content"));

        var ogDescription = doc.QuerySelector("head meta[property='og:description']");
        Assert.NotNull(ogDescription);
        Assert.Equal(
            "Edit-history findings from Finnish Wikipedia, with the evidence behind each.",
            ogDescription.GetAttribute("content"));

        Assert.Equal("Shared report · Ampersand of Truth", doc.Title);
        Assert.DoesNotContain("Sensitive Topic Investigation", doc.Head?.InnerHtml ?? "", StringComparison.OrdinalIgnoreCase);

        Assert.NotNull(doc.QuerySelector("[data-testid=shared-report]"));
        Assert.NotNull(doc.QuerySelector("[data-testid=shared-intro]"));
        Assert.NotNull(doc.QuerySelector($"#incident-{winterWar.Incidents[0].Id}"));
        Assert.Contains("Sensitive Topic Investigation", doc.Body?.TextContent ?? "");
        Assert.Contains("Endpoint shared report automated summary.", doc.Body?.TextContent ?? "");

        // 2. Junk token -> not-found state with generic message
        var junkResponse = await client.GetAsync("/r/junk");
        Assert.Equal(HttpStatusCode.OK, junkResponse.StatusCode);

        var junkHtml = await junkResponse.Content.ReadAsStringAsync();
        var junkDoc = new HtmlParser().ParseDocument(junkHtml);

        var junkRobots = junkDoc.QuerySelector("head meta[name='robots']");
        Assert.NotNull(junkRobots);
        Assert.Equal("noindex, nofollow", junkRobots.GetAttribute("content"));

        var notFoundEl = junkDoc.QuerySelector("[data-testid=shared-not-found]");
        Assert.NotNull(notFoundEl);
        Assert.Contains("This link is not valid or has been revoked.", notFoundEl.TextContent);
        Assert.Null(junkDoc.QuerySelector("[data-testid=shared-report]"));
    }
}
