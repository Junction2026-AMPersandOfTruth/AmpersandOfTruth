using System.Net;
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
using Microsoft.Extensions.DependencyInjection;

namespace AmpersandOfTruth.Tests.Features.Reports;

public sealed class ReportEndpointTests(AppFactory factory) : IClassFixture<AppFactory>
{
    [Fact]
    public async Task Unknown_id_returns_404()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync($"/library/{Guid.NewGuid()}/report.md");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Seeded_id_returns_200_markdown_with_attachment_header_and_supports_pinned_filter()
    {
        var fakeLlm = new FakeAnalysisModel().Returns(
            SummarizeInvestigationPrompt.Id,
            """{"summary":"Endpoint automated summary sentence."}""");

        await using var customFactory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IAnalysisModel>(fakeLlm);
                services.AddSingleton<IIncidentCorpus>(new FakeIncidentCorpus { Unavailable = true });
                services.AddSingleton<IRevisionStore>(new FakeRevisionStore { Unavailable = true });
            });
        });

        var store = customFactory.Services.GetRequiredService<LibraryStore>();
        var samples = customFactory.Services.GetRequiredService<SampleIncidents>();
        var winterWar = samples.Match("winter-war")
            ?? throw new InvalidOperationException("Expected winter-war sample fixture.");

        var snapshot = new SavedSnapshot(
            winterWar.Title,
            [new TopicArticle("fi", 1256, "Talvisota", 0.04)],
            winterWar.Incidents.Take(2).ToList(),
            new DateTimeOffset(2026, 11, 14, 10, 0, 0, TimeSpan.Zero));

        var savedId = await store.SaveAsync("endpoint-owner", "Winter War Filing", snapshot);
        await store.PinAsync(savedId, "endpoint-owner", winterWar.Incidents[0].Id!, "Primary finding.");

        var client = customFactory.CreateClient();

        var response = await client.GetAsync($"/library/{savedId}/report.md");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/markdown; charset=utf-8", response.Content.Headers.ContentType?.ToString());

        Assert.True(response.Headers.TryGetValues("Content-Disposition", out _)
            || response.Content.Headers.ContentDisposition is not null);
        var contentDisposition = response.Content.Headers.ContentDisposition?.ToString()
            ?? string.Join(",", response.Headers.GetValues("Content-Disposition"));
        Assert.Contains("attachment", contentDisposition);
        Assert.Contains("winter-war-report.md", contentDisposition);

        var fullMarkdown = await response.Content.ReadAsStringAsync();
        Assert.Contains("# Winter War Filing", fullMarkdown);
        Assert.Contains("Endpoint automated summary sentence.", fullMarkdown);
        Assert.Contains(winterWar.Incidents[0].ClaimEn!, fullMarkdown);
        Assert.Contains(winterWar.Incidents[1].ClaimEn!, fullMarkdown);
        Assert.Contains("## Appendix", fullMarkdown);

        var pinnedResponse = await client.GetAsync($"/library/{savedId}/report.md?pinned=1");
        Assert.Equal(HttpStatusCode.OK, pinnedResponse.StatusCode);
        var pinnedMarkdown = await pinnedResponse.Content.ReadAsStringAsync();
        Assert.Contains(winterWar.Incidents[0].ClaimEn!, pinnedMarkdown);
        Assert.DoesNotContain(winterWar.Incidents[1].ClaimEn!, pinnedMarkdown);
    }
}
