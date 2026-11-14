using AmpersandOfTruth.Tests.Features.Corpus;
using AmpersandOfTruth.Tests.Infrastructure;
using AmpersandOfTruth.Web.Features.Investigations;
using AmpersandOfTruth.Web.Features.Investigations.Pipeline;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AmpersandOfTruth.Tests.Features.Investigations;

/// <summary>The pipeline's revision SQL and stages 2–3 against the VPS corpus (LLM faked as down). Opt-in: <see cref="CorpusFactAttribute"/>.</summary>
[Trait("Category", "Corpus")]
public class PipelineCorpusIntegrationTests(AppFactory app) : IClassFixture<AppFactory>
{
    private const long Talvisota = 1256;

    private IRevisionStore Revisions => app.Services.GetRequiredService<IRevisionStore>();
    private IInvestigationCorpus Corpus => app.Services.GetRequiredService<IInvestigationCorpus>();

    [CorpusFact]
    public async Task Span_of_a_revert_is_its_undone_revisions_and_texts_load()
    {
        var revert = (await Corpus.RevertsAsync([Talvisota], perPage: 20)).First(r => r.RevertedCount >= 2);

        var span = await Revisions.SpanAsync(Talvisota, revert.RestoredRevId, revert.RevertedRevId, 50);

        Assert.Equal(revert.RevertedCount, span.Count);
        Assert.Equal(revert.RevertedRevId, span[^1].RevId);
        Assert.Equal(span.OrderBy(r => r.Timestamp).ThenBy(r => r.RevId).Select(r => r.RevId), span.Select(r => r.RevId));
        Assert.False(string.IsNullOrEmpty(await Revisions.TextAsync(Talvisota, revert.RestoredRevId)));
        Assert.True(await Revisions.RevisionCountAsync([Talvisota]) > 1000);
    }

    [CorpusFact]
    public async Task Stages_two_and_three_read_real_removed_edits()
    {
        var o = Options.Create(new InvestigationPipelineOptions { MaxCandidates = 10 });
        var scan = await new RevertCandidateSource(Corpus, Revisions, o).ScanAsync([new TopicArticle("fi", Talvisota, "Talvisota", 0)]);
        Assert.Equal(10, scan.Candidates.Count);

        var reader = new CorpusEditReader(Revisions, o);
        var readings = new List<EditReading>();
        foreach (var c in scan.Candidates) readings.Add(await reader.ReadAsync(c));

        Assert.All(readings, r => Assert.Equal(r.Edit.Revert.RevertedRevId, r.Span[^1].RevId));
        Assert.Contains(readings, r => r.Diff.Stats.IsSubstantive && r.InsertedText.Length > 0);
        var incidents = readings.Select(r => IncidentAssembler.Assemble(r, null).Incident).OfType<IncidentRecord>().ToList();
        Assert.NotEmpty(incidents);
        Assert.All(incidents, i => Assert.DoesNotMatch(@"\d+\.\d+\.\d+\.\d+", string.Join(" ", i.Edits.Select(e => e.UserDisplay + e.Summary))));
    }
}
