using AmpersandOfTruth.Tests.Features.Corpus;
using AmpersandOfTruth.Tests.Infrastructure;
using AmpersandOfTruth.Web.Features.Evidence;
using AmpersandOfTruth.Web.Features.Investigations;
using AmpersandOfTruth.Web.Features.Investigations.Overview;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace AmpersandOfTruth.Tests.Features.Investigations.Overview;

public sealed class ContestSignalsTests
{
    [Fact]
    public void FormatRailLine_formats_episodes_and_peak_month_and_passes_CopyRules()
    {
        var contested = new PageContest(
            PageId: 1150,
            EditWarEpisodes: 2,
            PingPongEpisodes: 3,
            BurstEpisodes: 4,
            ContestedScore: 20.5,
            PeakMonth: "2014-03");

        var line = ContestSignals.FormatRailLine(contested);
        Assert.Equal("Contested: 5 periods of back-and-forth edits \u00b7 peak Mar 2014", line);
        Assert.Empty(CopyRules.FindViolations(line!, includeJargon: true));

        var burstOnly = new PageContest(
            PageId: 250972,
            EditWarEpisodes: 0,
            PingPongEpisodes: 0,
            BurstEpisodes: 2,
            ContestedScore: 2.1,
            PeakMonth: "2017-08");

        var burstLine = ContestSignals.FormatRailLine(burstOnly);
        Assert.Equal("Contested: 2 bursts of edits \u00b7 peak Aug 2017", burstLine);
        Assert.Empty(CopyRules.FindViolations(burstLine!, includeJargon: true));

        // Zero episodes -> no line rendered
        var quiet = new PageContest(9186, 0, 0, 0, 2.4, "2005-04");
        Assert.Null(ContestSignals.FormatRailLine(quiet));
        Assert.Null(ContestSignals.FormatRailLine(null));
    }

    [Theory]
    [InlineData("graffiti_cleanup", "vandalism cleanup")]
    [InlineData("self_revert", "undone by the same account")]
    [InlineData("bot", "bot")]
    [InlineData("dispute", "disputed content")]
    [InlineData("edit_war", "back-and-forth edits")]
    [InlineData("other", null)]
    [InlineData("excluded", null)]
    [InlineData(null, null)]
    public void FormatRevertClass_maps_s1_classes_to_plain_words(string? input, string? expected)
    {
        var actual = ContestSignals.FormatRevertClass(input);
        Assert.Equal(expected, actual);
        if (actual is not null)
        {
            Assert.Empty(CopyRules.FindViolations(actual, includeJargon: true));
        }
    }

    [Fact]
    public async Task MariaDbContestSignals_caches_hits_and_misses_and_does_not_cache_failures()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 14, 12, 0, 0, TimeSpan.Zero));
        int contestCalls = 0;
        int classCalls = 0;
        bool fail = true;

        var service = new MariaDbContestSignals(
            (ids, _) =>
            {
                contestCalls++;
                if (fail) throw new InvalidOperationException("s4_page_contest missing");
                var dict = new Dictionary<long, PageContest>();
                foreach (var id in ids)
                {
                    if (id == 1150)
                    {
                        dict[1150] = new PageContest(1150, 4, 17, 20, 28.17, "2007-08");
                    }
                }

                return Task.FromResult<IReadOnlyDictionary<long, PageContest>>(dict);
            },
            (revs, _) =>
            {
                classCalls++;
                if (fail) throw new InvalidOperationException("s1_revert missing");
                var dict = new Dictionary<(long PageId, long RevertingRevId), string>();
                foreach (var key in revs)
                {
                    if (key == (1150, 5002))
                    {
                        dict[key] = "graffiti_cleanup";
                    }
                }

                return Task.FromResult<IReadOnlyDictionary<(long PageId, long RevertingRevId), string>>(dict);
            },
            clock,
            NullLogger<MariaDbContestSignals>.Instance);

        // Failures return empty dictionaries without caching
        Assert.Empty(await service.PageContestsAsync([1150, 1256]));
        Assert.Empty(await service.RevertClassesAsync([(1150, 5002)]));
        Assert.Equal(1, contestCalls);
        Assert.Equal(1, classCalls);

        // Recovery queries DB and caches both present (1150) and absent (1256) entries
        fail = false;
        var contests = await service.PageContestsAsync([1150, 1256]);
        Assert.Single(contests);
        Assert.True(contests.ContainsKey(1150));

        var classes = await service.RevertClassesAsync([(1150, 5002), (1150, 5003)]);
        Assert.Equal("graffiti_cleanup", classes[(1150, 5002)]);
        Assert.Equal(2, contestCalls);
        Assert.Equal(2, classCalls);

        // Repeated calls within 1h hit cache even for previously absent keys
        clock.Advance(TimeSpan.FromMinutes(30));
        Assert.Single(await service.PageContestsAsync([1150, 1256]));
        Assert.Single(await service.RevertClassesAsync([(1150, 5002), (1150, 5003)]));
        Assert.Equal(2, contestCalls);
        Assert.Equal(2, classCalls);
    }
}

[Trait("Category", "Corpus")]
public sealed class ContestSignalsCorpusIntegrationTests(AppFactory app) : IClassFixture<AppFactory>
{
    private const long SuomenSisallissota = 1150;

    private IContestSignals Signals => app.Services.GetRequiredService<IContestSignals>();
    private IInvestigationCorpus Corpus => app.Services.GetRequiredService<IInvestigationCorpus>();

    [CorpusFact]
    public async Task Suomen_sisallissota_1150_has_s4_page_contest()
    {
        var contests = await Signals.PageContestsAsync([SuomenSisallissota, 999_999_999]);

        Assert.True(contests.TryGetValue(SuomenSisallissota, out var contest));
        Assert.False(contests.ContainsKey(999_999_999));
        Assert.True(contest.HasEpisodes);
        Assert.True(contest.EditWarEpisodes > 0);
        Assert.True(contest.ContestedScore > 0);

        var line = ContestSignals.FormatRailLine(contest);
        Assert.NotNull(line);
        Assert.StartsWith("Contested:", line, StringComparison.Ordinal);
        Assert.Contains("periods of back-and-forth edits", line, StringComparison.Ordinal);
    }

    [CorpusFact]
    public async Task Suomen_sisallissota_1150_reverts_resolve_s1_classes()
    {
        var reverts = await Corpus.RevertsAsync([SuomenSisallissota], perPage: 20);
        Assert.NotEmpty(reverts);

        var keys = reverts.Select(static r => (r.PageId, r.RevId)).ToList();
        var classes = await Signals.RevertClassesAsync(keys);

        Assert.NotEmpty(classes);
        string[] known = ["dispute", "edit_war", "graffiti_cleanup", "self_revert", "bot", "other", "excluded"];
        Assert.All(classes.Values, cls => Assert.Contains(cls, known));
    }
}
