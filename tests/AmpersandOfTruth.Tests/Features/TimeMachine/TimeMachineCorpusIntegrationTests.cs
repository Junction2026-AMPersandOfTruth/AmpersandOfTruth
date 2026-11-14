using System.Diagnostics;
using AmpersandOfTruth.Tests.Features.Corpus;
using AmpersandOfTruth.Tests.Infrastructure;
using AmpersandOfTruth.Web.Features.Evidence;
using AmpersandOfTruth.Web.Features.TimeMachine;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace AmpersandOfTruth.Tests.Features.TimeMachine;

[Trait("Category", "Corpus")]
public sealed class TimeMachineCorpusIntegrationTests(AppFactory app, ITestOutputHelper output) : IClassFixture<AppFactory>
{
    private const long SuomenSisallissota = 1150;
    private const long Talvisota = 1256;
    private const long MainilanLaukaukset = 8295;

    private ITimeMachineCorpus Corpus => app.Services.GetRequiredService<ITimeMachineCorpus>();

    private TimeMachineService Service => app.Services.GetRequiredService<TimeMachineService>();

    [CorpusFact]
    public async Task Talvisota_and_Suomen_sisallissota_read_historical_revisions_highlights_and_diffs()
    {
        var range = await Corpus.RangeAsync(Talvisota);
        Assert.NotNull(range);
        Assert.True(range.Value.Count > 100);

        var beforeFirst = await Corpus.RevisionAtAsync(Talvisota, new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        Assert.Null(beforeFirst);

        var sw = Stopwatch.StartNew();
        var snap2014 = await Service.ReadAtAsync(
            pageId: Talvisota,
            atUtc: TimeMachineService.EndOfUtcDay(new DateOnly(2014, 3, 1)),
            compareDate: new DateOnly(2020, 3, 1));
        sw.Stop();

        Assert.NotNull(snap2014);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(30), $"ReadAtAsync took {sw.Elapsed.TotalMilliseconds:F0} ms (> 30000 ms).");
        Assert.False(snap2014.BeforeFirst);
        Assert.NotNull(snap2014.Revision);
        Assert.True(snap2014.Revision.Ts <= new DateTime(2014, 3, 1, 23, 59, 59, DateTimeKind.Utc));
        Assert.Equal("Talvisota", snap2014.TitleFi);
        Assert.NotEmpty(snap2014.Prose);
        Assert.NotEmpty(snap2014.Blocks);
        Assert.NotNull(snap2014.Comparison);
        Assert.NotNull(snap2014.Comparison.RevisionB);
        Assert.True(snap2014.Comparison.RevisionB.RevId > snap2014.Revision.RevId);

        output.WriteLine(
            $"Talvisota (page_id={Talvisota}): total={range.Value.Count}, first={range.Value.First:O}, last={range.Value.Last:O}");
        output.WriteLine(
            $"Talvisota at 2014-03-01: rev_id={snap2014.Revision.RevId}, ts={snap2014.Revision.Ts:O}, editor={EditorDisplay.For(snap2014.Revision.User).DisplayName}, bytes={snap2014.Revision.Bytes}, proseChars={snap2014.Prose.Length}, truncated={snap2014.IsTruncated}");
        output.WriteLine(
            $"Talvisota compare 2020-03-01: rev_id={snap2014.Comparison.RevisionB.RevId}, ts={snap2014.Comparison.RevisionB.Ts:O}, segments={snap2014.Comparison.Passage.Diff.Segments.Count}");

        // Check incident highlight on Suomen sisällissota (cw18-01, rev 17895463 vs parent 17895434)
        var cwSnap = await Service.ReadAtAsync(
            pageId: SuomenSisallissota,
            atUtc: new DateTime(2019, 1, 31, 19, 51, 22, DateTimeKind.Utc),
            revId: 17895463,
            highlightRevId: 17895463,
            highlightParentRevId: 17895434);

        Assert.NotNull(cwSnap);
        Assert.Equal(17895463, cwSnap.Revision?.RevId);
        Assert.NotNull(cwSnap.Highlight);
        output.WriteLine(
            $"Suomen sisällissota (page_id={SuomenSisallissota}) rev 17895463: foundInProse={cwSnap.Highlight.FoundInProse}, proseChars={cwSnap.Prose.Length}, truncated={cwSnap.IsTruncated}");

        // Check inline prose highlight on Mainilan laukaukset (ww-02, rev 15356346 vs parent 14834809)
        var mlSnap = await Service.ReadAtAsync(
            pageId: MainilanLaukaukset,
            atUtc: new DateTime(2015, 11, 25, 4, 36, 41, DateTimeKind.Utc),
            revId: 15356346,
            highlightRevId: 15356346,
            highlightParentRevId: 14834809);

        Assert.NotNull(mlSnap);
        Assert.Equal(15356346, mlSnap.Revision?.RevId);
        Assert.NotNull(mlSnap.Highlight);
        Assert.True(mlSnap.Highlight.FoundInProse);
        output.WriteLine(
            $"Mainilan laukaukset (page_id={MainilanLaukaukset}) rev 15356346: foundInProse={mlSnap.Highlight.FoundInProse}, ranges={mlSnap.Highlight.Ranges.Count}, proseChars={mlSnap.Prose.Length}");
    }

    [CorpusFact]
    public async Task Unknown_article_returns_null_range_and_revision()
    {
        Assert.Null(await Corpus.RangeAsync(999_999_999));
        Assert.Null(await Corpus.RevisionAtAsync(999_999_999, DateTime.UtcNow));
        Assert.Null(await Corpus.TextAsync(999_999_999, 1));
    }
}
