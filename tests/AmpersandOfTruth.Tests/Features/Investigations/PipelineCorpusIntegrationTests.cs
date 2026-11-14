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

    [CorpusFact]
    public async Task Conflict_episodes_are_read_from_the_s4_tables()
    {
        // Suomen sisällissota has 41 default S4 episodes (research/31-s4-conflict/README.md), edit wars and ping-pongs among them.
        const long civilWar = 1150;
        var episodes = app.Services.GetRequiredService<IConflictEpisodes>();
        var reverts = await Corpus.RevertsAsync([civilWar], perPage: 2000);

        var found = await episodes.ContainingAsync(civilWar, reverts.Select(r => r.RevId).ToList());

        Assert.Contains(found, e => e.Kind == ConflictKind.EditWar);
        Assert.Contains(found, e => e.Kind == ConflictKind.PingPong);
        Assert.All(found, e => Assert.True(e.Start <= e.End && e.RevIds.Count > 0 && e.EditCount >= e.RevIds.Count));
        Assert.Empty(await episodes.ContainingAsync(Talvisota, reverts.Select(r => r.RevId).Take(50).ToList()));   // other page: none
    }

    [CorpusFact]
    public async Task Readings_of_real_removed_edits_carry_the_s4_and_s8_signals()
    {
        var o = Options.Create(new InvestigationPipelineOptions { MaxCandidates = 40 });
        var scan = await new RevertCandidateSource(Corpus, Revisions, o).ScanAsync([new TopicArticle("fi", 1150, "Suomen sisällissota", 0)]);
        var reader = new CorpusEditReader(Revisions, o, app.Services.GetRequiredService<IConflictEpisodes>());

        var readings = new List<EditReading>();
        foreach (var c in scan.Candidates) readings.Add(await reader.ReadAsync(c));

        Assert.All(readings, r => Assert.NotNull(r.Conflicts));   // null = research.s4_* could not be read
        Assert.All(readings.Where(r => r.EditWar is not null), r => Assert.Contains(r.Edit.Revert.RevId, r.EditWar!.RevIds));
    }

    [CorpusFact]
    public async Task Fi_lookups_find_talvisota_by_page_id_and_by_wikidata_id()
    {
        var byId = Assert.Single(await Corpus.FiArticlesAsync([Talvisota, 999_999_999]));
        Assert.Equal("Talvisota", byId.Title);
        Assert.NotNull(byId.WikidataId);

        var siblings = await Corpus.FiSiblingsAsync([byId.WikidataId!, "Q0"]);

        Assert.Equal(Talvisota, Assert.Single(siblings).Value.PageId);
    }

    [CorpusFact]
    public async Task The_first_restore_of_the_restored_content_is_the_revert_itself()
    {
        var revert = (await Corpus.RevertsAsync([Talvisota], perPage: 20)).First();
        var through = await Revisions.SpanAsync(Talvisota, revert.RestoredRevId, revert.RevId, 50);
        var undone = through[0];
        var restoredSha1 = through[^1].Sha1!; // the revert has the restored revision's content

        var restores = await Revisions.RestoresAsync(Talvisota, undone.Timestamp, undone.RevId, [restoredSha1], 5);

        Assert.Equal(revert.RevId, restores[0].RevId);
        Assert.All(restores, r => Assert.Equal(restoredSha1, r.Sha1));
    }

    [CorpusFact]
    public async Task Returns_to_the_restored_state_match_restores_of_its_sha1_and_start_with_the_revert()
    {
        var revert = (await Corpus.RevertsAsync([Talvisota], perPage: 20)).First();
        var through = await Revisions.SpanAsync(Talvisota, revert.RestoredRevId, revert.RevId, 50);
        var undone = through[0];
        var restoredSha1 = through[^1].Sha1!;

        var returns = await Revisions.ReturnsToAsync(Talvisota, undone.Timestamp, undone.RevId, revert.RestoredRevId, 5);
        var restores = await Revisions.RestoresAsync(Talvisota, undone.Timestamp, undone.RevId, [restoredSha1], 5);

        Assert.Equal(revert.RevId, returns[0].RevId);
        Assert.Equal(restores.Select(r => r.RevId), returns.Select(r => r.RevId));
    }

    [CorpusFact]
    public async Task Re_removals_on_a_contested_page_follow_a_re_insertion_and_restore_the_pre_edit_content()
    {
        var o = Options.Create(new InvestigationPipelineOptions { MaxCandidates = 40 });
        var scan = await new RevertCandidateSource(Corpus, Revisions, o).ScanAsync([new TopicArticle("fi", 1150, "Suomen sisällissota", 0)]);
        var reader = new CorpusEditReader(Revisions, o);

        foreach (var c in scan.Candidates)
        {
            var r = await reader.ReadAsync(c);
            if (r.ReRemovals.Count == 0) continue;

            var restored = (await Revisions.ReturnsToAsync(1150, c.Revert.Timestamp, c.Revert.RevId, c.Revert.RestoredRevId, 1)).Single().Sha1;
            Assert.NotEmpty(r.Reinsertions);
            Assert.All(r.ReRemovals, x => Assert.Equal(restored, x.Sha1));
            Assert.True(r.Reinsertions[0].Timestamp <= r.ReRemovals[0].Timestamp);
            Assert.All(r.ReRemovals, x => Assert.True(x.Timestamp >= c.Revert.Timestamp));
        }
    }
}
