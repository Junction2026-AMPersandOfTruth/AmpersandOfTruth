using AmpersandOfTruth.Web.Features.Evidence;
using AmpersandOfTruth.Web.Features.Investigations.Pipeline;

namespace AmpersandOfTruth.Tests.Features.Investigations;

/// <summary>P1.S4 as a tier input (#31): which conflict episodes make a removed edit part of a back-and-forth.</summary>
public class EditWarsTests
{
    private static readonly DateTime Day = new(2008, 3, 25, 18, 0, 0, DateTimeKind.Utc);
    private static readonly RevisionRow[] Span = [new(1150, 101, 100, Day, "A", null, 10), new(1150, 102, 101, Day, "A", null, 12)];
    private const long Removal = 103;

    private static ConflictEpisode Episode(long id, ConflictKind kind, int undos, params long[] revIds) =>
        new(id, kind, Day, Day.AddDays(1), undos, 20, revIds);

    [Theory]
    [InlineData(ConflictKind.EditWar, true)]
    [InlineData(ConflictKind.PingPong, true)]
    [InlineData(ConflictKind.Burst, false)]   // a busy day is attention, not conflict
    public void Only_edit_wars_and_ping_pongs_count(ConflictKind kind, bool counts) =>
        Assert.Equal(counts, EditWars.Of([Episode(1, kind, 5, 102, Removal)], Span, Removal) is not null);

    [Theory]
    [InlineData(new long[] { 101, 102 })]   // added during the episode, removed after it
    [InlineData(new long[] { Removal })]    // an old edit removed during the episode
    public void The_removal_and_a_removed_revision_must_both_be_inside(long[] members) =>
        Assert.Null(EditWars.Of([Episode(1, ConflictKind.EditWar, 5, members)], Span, Removal));

    [Fact]
    public void An_edit_war_goes_before_a_ping_pong_and_more_undos_before_fewer()
    {
        var episodes = new[]
        {
            Episode(1, ConflictKind.PingPong, 9, 101, Removal),
            Episode(2, ConflictKind.EditWar, 3, 102, Removal),
            Episode(3, ConflictKind.EditWar, 6, 101, 102, Removal),
            Episode(4, ConflictKind.Burst, 30, 101, 102, Removal),
        };

        Assert.Equal(3, EditWars.Of(episodes, Span, Removal)!.EpisodeId);
    }

    [Fact]
    public void No_episodes_or_unknown_episodes_are_no_edit_war()
    {
        Assert.Null(EditWars.Of([], Span, Removal));
        Assert.Null(EditWars.Of(null, Span, Removal));
    }

    [Fact]
    public void The_description_gives_counts_and_dates_and_passes_the_copy_rules()
    {
        var twoDays = EditWars.Describe(Episode(1, ConflictKind.EditWar, 6, Removal));
        var oneDay = EditWars.Describe(Episode(1, ConflictKind.PingPong, 1, Removal) with { End = Day.AddHours(2) });

        Assert.Equal("6 undoing edits between 25 March 2008 and 26 March 2008", twoDays);
        Assert.Equal("1 undoing edit on 25 March 2008", oneDay);
        Assert.Empty(CopyRules.FindViolations(twoDays));
    }

    [Theory]
    [InlineData("edit_war", ConflictKind.EditWar)]
    [InlineData("ping_pong", ConflictKind.PingPong)]
    [InlineData("burst", ConflictKind.Burst)]
    [InlineData("something_new", null)]
    public void Kinds_are_read_as_the_experiment_wrote_them(string stored, ConflictKind? kind) =>
        Assert.Equal(kind, MariaDbConflictEpisodes.ParseKind(stored));
}
