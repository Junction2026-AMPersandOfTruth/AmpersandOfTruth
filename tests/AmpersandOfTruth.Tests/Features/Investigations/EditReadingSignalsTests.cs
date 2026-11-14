using AmpersandOfTruth.Tests.Infrastructure;
using AmpersandOfTruth.Web.Features.Investigations;
using AmpersandOfTruth.Web.Features.Investigations.Cache;
using AmpersandOfTruth.Web.Features.Investigations.Pipeline;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace AmpersandOfTruth.Tests.Features.Investigations;

/// <summary>Stage 3a attaches the per-edit signals ported from P1.S4 (conflict episodes) and P1.S8 (citation changes).</summary>
public sealed class EditReadingSignalsTests : IDisposable
{
    private const string Cited = "Talvisota alkoi 30. marraskuuta 1939, kun Neuvostoliitto hyökkäsi Suomeen.<ref>Manninen 1994, s. 28.</ref>";
    private const string Uncited = "Talvisota alkoi 30. marraskuuta 1939, kun Suomi hyökkäsi ensin Neuvostoliittoon Mainilassa.";

    private readonly PipelineKit _kit = new();
    private readonly TestDb _db = new();
    private static readonly RemovedEdit Edit = new(new TopicArticle("fi", 1256, "Talvisota", 0), FakeInvestigationCorpus.Revert(1256, 2000));

    public void Dispose() => _db.Dispose();

    private CorpusEditReader Reader() => new(_kit.Revisions, Microsoft.Extensions.Options.Options.Create(_kit.Options), _kit.Conflicts);

    private CachedEditReader Cached() => new(
        Reader(), Microsoft.Extensions.Options.Options.Create(_kit.Options), _db.Factory, TimeProvider.System, NullLogger<CachedEditReader>.Instance);

    private async Task<int> CachedRows()
    {
        await using var db = _db.CreateContext();
        return await db.Set<EditReadingCacheRow>().CountAsync();
    }

    [Fact]
    public async Task The_reading_carries_the_episodes_around_the_edit_and_its_removal()
    {
        _kit.Conflicts.Episode(1256, ConflictKind.EditWar, 1999, 2000, 2001).Episode(5263, ConflictKind.EditWar, 1999, 2000);

        var reading = await Reader().ReadAsync(Edit);

        var episode = Assert.Single(reading.Conflicts!);   // the one on this page, with only the revisions asked for
        Assert.Equal([1999L, 2000L], episode.RevIds);
        Assert.Same(episode, reading.EditWar);
    }

    [Fact]
    public async Task An_edit_outside_every_episode_is_no_edit_war()
    {
        _kit.Conflicts.Episode(1256, ConflictKind.EditWar, 500, 501).Episode(1256, ConflictKind.Burst, 1999, 2000);

        var reading = await Reader().ReadAsync(Edit);

        Assert.Equal(ConflictKind.Burst, Assert.Single(reading.Conflicts!).Kind);
        Assert.Null(reading.EditWar);
    }

    [Fact]
    public async Task The_reading_lists_the_citations_the_edit_took_away()
    {
        _kit.Revisions.Rows.Clear();
        _kit.Revisions.Edit(1256, 1998, 1999, "192.0.2.1", Cited, Uncited);

        var reading = await Reader().ReadAsync(Edit);

        var change = Assert.Single(reading.SourceChanges);
        Assert.Equal(SourceChangeKind.CitationRemoved, change.Kind);
        Assert.Equal("<ref>Manninen 1994, s. 28.</ref>", change.Evidence);
    }

    [Fact]
    public async Task Unreadable_episode_tables_leave_the_signal_unknown_without_costing_the_reading()
    {
        _kit.Conflicts.Unavailable = true;

        var reading = await Reader().ReadAsync(Edit);

        Assert.Null(reading.Conflicts);
        Assert.Null(reading.EditWar);
        Assert.True(reading.Diff.Stats.IsSubstantive);
    }

    [Fact]
    public async Task Switched_off_signals_are_not_computed()
    {
        _kit.Revisions.Rows.Clear();
        _kit.Revisions.Edit(1256, 1998, 1999, "192.0.2.1", Cited, Uncited);
        _kit.Conflicts.Episode(1256, ConflictKind.EditWar, 1999, 2000);
        _kit.Options.EditWarSignal = false;
        _kit.Options.SourceChangeSignal = false;

        var reading = await Reader().ReadAsync(Edit);

        Assert.Empty(reading.Conflicts!);
        Assert.Empty(reading.SourceChanges);
        Assert.Equal(0, _kit.Conflicts.Calls);
    }

    [Fact]
    public async Task A_cached_reading_keeps_both_signals()
    {
        _kit.Revisions.Rows.Clear();
        _kit.Revisions.Edit(1256, 1998, 1999, "192.0.2.1", Cited, Uncited);
        _kit.Conflicts.Episode(1256, ConflictKind.PingPong, 1999, 2000);
        var reader = Cached();

        var fresh = await reader.ReadAsync(Edit);
        var cached = await reader.ReadAsync(Edit);

        Assert.Equal(1, _kit.Conflicts.Calls);
        Assert.Equal(fresh.SourceChanges, cached.SourceChanges);
        var (a, b) = (Assert.Single(fresh.Conflicts!), Assert.Single(cached.Conflicts!));
        Assert.Equal((a.EpisodeId, a.Kind, a.Start, a.End, a.UndoCount, a.EditCount), (b.EpisodeId, b.Kind, b.Start, b.End, b.UndoCount, b.EditCount));
        Assert.Equal(a.RevIds, b.RevIds);
        Assert.Equal(ConflictKind.PingPong, cached.EditWar!.Kind);
    }

    [Fact]
    public async Task A_reading_with_unknown_episodes_is_not_cached_so_it_is_read_again_once_they_are_back()
    {
        _kit.Conflicts.Episode(1256, ConflictKind.EditWar, 1999, 2000);
        _kit.Conflicts.Unavailable = true;
        var reader = Cached();

        var first = await reader.ReadAsync(Edit);
        Assert.Null(first.Conflicts);
        Assert.Equal(0, await CachedRows());

        _kit.Conflicts.Unavailable = false;
        var second = await reader.ReadAsync(Edit);

        Assert.NotNull(second.EditWar);
        Assert.Equal(1, await CachedRows());
    }

    [Fact]
    public void Switching_a_signal_off_is_a_different_cached_reading_and_a_different_run()
    {
        var on = Cached().KeyFor(Edit);
        _kit.Options.SourceChangeSignal = false;
        var off = Cached().KeyFor(Edit);

        Assert.NotEqual(on, off);
        Assert.True(off.Length <= 100);   // the column's max length
    }
}
