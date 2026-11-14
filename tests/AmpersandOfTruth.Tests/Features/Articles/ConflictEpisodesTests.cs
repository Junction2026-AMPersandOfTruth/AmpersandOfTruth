using AmpersandOfTruth.Web.Features.Articles;
using AmpersandOfTruth.Web.Features.Evidence;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace AmpersandOfTruth.Tests.Features.Articles;

public sealed class ConflictEpisodesTests
{
    [Fact]
    public void FormatLabel_matches_spec_wording_and_passes_CopyRules_including_jargon()
    {
        var editWar = new ConflictEpisode(
            EpisodeId: 1,
            PageId: 1150,
            Kind: "edit_war",
            StartTs: new DateTime(2014, 3, 3, 10, 0, 0, DateTimeKind.Utc),
            EndTs: new DateTime(2014, 3, 19, 18, 30, 0, DateTimeKind.Utc),
            RevertCount: 14,
            NamedParticipants: 3,
            AnonParticipants: 2,
            EditCount: 25,
            ProtectionHint: false,
            Score: 8.5);

        var label = ConflictEpisodes.FormatLabel(editWar);
        Assert.Equal(
            "Back-and-forth edits \u00b7 3\u201319 Mar 2014 \u00b7 14 removals \u00b7 3 named and 2 anonymous accounts",
            label);
        Assert.Empty(CopyRules.FindViolations(label, includeJargon: true));

        var burst = editWar with
        {
            EpisodeId = 2,
            Kind = "burst",
            StartTs = new DateTime(2020, 3, 10, 8, 0, 0, DateTimeKind.Utc),
            EndTs = new DateTime(2020, 3, 10, 20, 0, 0, DateTimeKind.Utc),
            RevertCount = 1,
            NamedParticipants = 4,
            AnonParticipants = 0,
        };

        var burstLabel = ConflictEpisodes.FormatLabel(burst);
        Assert.Equal(
            "Burst of edits \u00b7 10 Mar 2020 \u00b7 1 removal \u00b7 4 named accounts",
            burstLabel);
        Assert.Empty(CopyRules.FindViolations(burstLabel, includeJargon: true));
    }

    [Fact]
    public void TryFormatBusiestSummary_uses_busiest_default_edit_war_only()
    {
        IReadOnlyList<ConflictEpisode> episodes =
        [
            new ConflictEpisode(1, 1150, "ping_pong", new DateTime(2019, 2, 6, 0, 0, 0, DateTimeKind.Utc), new DateTime(2019, 2, 11, 0, 0, 0, DateTimeKind.Utc), 25, 6, 0, 30, true, 9.0),
            new ConflictEpisode(2, 1150, "edit_war", new DateTime(2010, 2, 8, 0, 0, 0, DateTimeKind.Utc), new DateTime(2010, 2, 11, 0, 0, 0, DateTimeKind.Utc), 5, 4, 0, 12, false, 6.0),
            new ConflictEpisode(3, 1150, "edit_war", new DateTime(2014, 3, 3, 0, 0, 0, DateTimeKind.Utc), new DateTime(2014, 3, 19, 0, 0, 0, DateTimeKind.Utc), 14, 3, 2, 24, false, 8.2),
        ];

        var summary = ConflictEpisodes.TryFormatBusiestSummary(episodes);
        Assert.Equal("Most contested period: Mar 2014 (back-and-forth edits, 14 removals).", summary);
        Assert.Empty(CopyRules.FindViolations(summary!, includeJargon: true));

        // Without edit_war episodes, returns null so caller keeps monthly heuristic summary
        Assert.Null(ConflictEpisodes.TryFormatBusiestSummary([episodes[0]]));
    }

    [Fact]
    public async Task MariaDbConflictEpisodes_caches_for_one_hour_and_does_not_cache_failures()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 14, 12, 0, 0, TimeSpan.Zero));
        int calls = 0;
        bool shouldThrow = true;

        var ep = new ConflictEpisode(
            1, 1150, "edit_war",
            new DateTime(2014, 3, 3, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2014, 3, 19, 0, 0, 0, DateTimeKind.Utc),
            14, 3, 2, 20, false, 7.5);

        var service = new MariaDbConflictEpisodes(
            (_, _) =>
            {
                calls++;
                if (shouldThrow) throw new InvalidOperationException("table missing");
                return Task.FromResult<IReadOnlyList<ConflictEpisode>>([ep]);
            },
            clock,
            NullLogger<MariaDbConflictEpisodes>.Instance);

        // Failure degrades to empty and is not cached
        Assert.Empty(await service.ForPageAsync(1150));
        Assert.Equal(1, calls);

        shouldThrow = false;
        var loaded = await service.ForPageAsync(1150);
        Assert.Single(loaded);
        Assert.Equal(2, calls);

        // Cached within 1h TTL
        clock.Advance(TimeSpan.FromMinutes(30));
        Assert.Single(await service.ForPageAsync(1150));
        Assert.Equal(2, calls);

        // Refreshes after 1h TTL expires
        clock.Advance(TimeSpan.FromMinutes(31));
        Assert.Single(await service.ForPageAsync(1150));
        Assert.Equal(3, calls);
    }
}
