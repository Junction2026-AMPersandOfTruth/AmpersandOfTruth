using System.Diagnostics;
using AmpersandOfTruth.Tests.Features.Corpus;
using AmpersandOfTruth.Tests.Infrastructure;
using AmpersandOfTruth.Web.Features.Accounts;
using AmpersandOfTruth.Web.Features.Corpus;
using Microsoft.Extensions.DependencyInjection;
using MySqlConnector;
using Xunit.Abstractions;

namespace AmpersandOfTruth.Tests.Features.Accounts;

[Trait("Category", "Corpus")]
public sealed class AccountCorpusIntegrationTests(AppFactory factory, ITestOutputHelper output) : IClassFixture<AppFactory>
{
    private MariaDbAccountCorpus NewCorpus() => factory.Services.GetRequiredService<MariaDbAccountCorpus>();

    [CorpusFact]
    public async Task Top_account_returns_within_5s_with_consistent_counts()
    {
        var connFactory = factory.Services.GetRequiredService<CorpusConnectionFactory>();
        string topNamedUser;
        await using (var conn = await connFactory.OpenAsync())
        await using (var cmd = new MySqlCommand(
            "SELECT `user`, COUNT(*) FROM revision WHERE anonymous = 0 AND `user` IS NOT NULL GROUP BY `user` ORDER BY 2 DESC LIMIT 1",
            conn))
        await using (var r = await cmd.ExecuteReaderAsync())
        {
            Assert.True(await r.ReadAsync());
            topNamedUser = r.GetString(0);
        }

        var sw = Stopwatch.StartNew();
        var facts = await NewCorpus().FactsAsync(topNamedUser);
        sw.Stop();
        output.WriteLine($"{topNamedUser}: {facts.Edits} edits, {facts.Articles} articles, {facts.RemovedEdits} removed in {sw.Elapsed.TotalMilliseconds:F0} ms");

        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5), $"FactsAsync({topNamedUser}) took {sw.Elapsed}");
        Assert.Equal(topNamedUser, facts.User);
        Assert.True(facts.Edits >= 10_000, $"Expected >=10k edits for {topNamedUser}, got {facts.Edits}");
        Assert.True(facts.Articles > 0 && facts.Articles <= facts.Edits);
        Assert.InRange(facts.RemovedEdits, 0, facts.Edits);
        Assert.NotNull(facts.First);
        Assert.NotNull(facts.Last);
        Assert.True(facts.First <= facts.Last);

        Assert.Equal(facts.Edits, facts.PerYear.Sum(y => y.Edits));
        Assert.Equal(facts.PerYear.Select(y => y.Year).OrderBy(y => y), facts.PerYear.Select(y => y.Year));

        Assert.Equal(20, facts.TopArticles.Count);
        Assert.Equal(
            facts.TopArticles.Select(a => a.Edits).OrderByDescending(e => e),
            facts.TopArticles.Select(a => a.Edits));
        Assert.All(facts.TopArticles, a =>
        {
            Assert.True(a.PageId > 0);
            Assert.False(string.IsNullOrWhiteSpace(a.Title));
            Assert.True(a.Edits > 0);
            Assert.InRange(a.RemovedEdits, 0, a.Edits);
        });

        // Also verify a ~10k+ edit wide-article-spread account (Ipr1Bot edits ~9,537 distinct articles).
        var wideSw = Stopwatch.StartNew();
        var wide = await NewCorpus().FactsAsync("Ipr1Bot");
        wideSw.Stop();
        output.WriteLine($"Ipr1Bot: {wide.Edits} edits, {wide.Articles} articles, {wide.RemovedEdits} removed in {wideSw.Elapsed.TotalMilliseconds:F0} ms");

        Assert.True(wideSw.Elapsed < TimeSpan.FromSeconds(5), $"FactsAsync(Ipr1Bot) took {wideSw.Elapsed}");
        Assert.True(wide.Edits >= 10_000);
        Assert.True(wide.Articles >= 9_000);
        Assert.InRange(wide.RemovedEdits, 0, wide.Edits);
    }

    [CorpusFact]
    public async Task Known_reverted_and_unknown_accounts_match_corpus()
    {
        var corpus = NewCorpus();

        var sw1 = Stopwatch.StartNew();
        var mapsed = await corpus.FactsAsync("Mapsed");
        sw1.Stop();
        output.WriteLine($"Mapsed: {mapsed.Edits} edits, {mapsed.RemovedEdits} removed in {sw1.Elapsed.TotalMilliseconds:F0} ms");
        Assert.Equal(39, mapsed.Edits);
        Assert.Equal(10, mapsed.Articles);
        Assert.Equal(39, mapsed.RemovedEdits);
        Assert.Equal(10, mapsed.TopArticles.Count);
        Assert.Equal(39, mapsed.TopArticles.Sum(a => a.RemovedEdits));

        var sw2 = Stopwatch.StartNew();
        var missing = await corpus.FactsAsync("DefinitelyNotAnAccountInFiWiki_98");
        sw2.Stop();
        output.WriteLine($"Missing: {sw2.Elapsed.TotalMilliseconds:F0} ms");
        Assert.Equal(0, missing.Edits);
        Assert.Equal(0, missing.Articles);
        Assert.Equal(0, missing.RemovedEdits);
        Assert.Null(missing.First);
        Assert.Null(missing.Last);
        Assert.Empty(missing.PerYear);
        Assert.Empty(missing.TopArticles);
    }

    [CorpusFact]
    public async Task Large_account_profile_from_s6_returns_consistent_facts()
    {
        var profileSource = factory.Services.GetRequiredService<MariaDbEditorProfileSource>();

        var sw = Stopwatch.StartNew();
        var botProfile = await profileSource.ProfileAsync("Ipr1Bot");
        sw.Stop();
        Assert.NotNull(botProfile);
        output.WriteLine($"Ipr1Bot profile: {botProfile.Edits} edits, {botProfile.ActiveDays} active days, isBot={botProfile.IsBot} in {sw.Elapsed.TotalMilliseconds:F1} ms");

        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5), $"ProfileAsync(Ipr1Bot) took {sw.Elapsed}");
        Assert.Equal("Ipr1Bot", botProfile.User);
        Assert.True(botProfile.IsBot);
        Assert.True(botProfile.Edits >= 10_000);
        Assert.InRange(botProfile.NonNullEdits, 1, botProfile.Edits);
        Assert.InRange(botProfile.ActiveDays, 2, botProfile.Edits);
        Assert.NotNull(botProfile.FirstTs);
        Assert.NotNull(botProfile.LastTs);
        Assert.True(botProfile.FirstTs < botProfile.LastTs);
        Assert.NotNull(botProfile.TopPageId);
        Assert.True(botProfile.TopPageId > 0);
        Assert.False(string.IsNullOrWhiteSpace(botProfile.TopPageTitle));
        Assert.InRange(botProfile.TopPageShare, 0.0, 1.0);
        Assert.InRange(botProfile.RevertedEditsByOthers, 0, botProfile.NonNullEdits);
        Assert.True(botProfile.RevertsMadeOthers >= 0);
        Assert.InRange(botProfile.CustomSummaryUsage, 0.0, 1.0);

        var humanProfile = await profileSource.ProfileAsync("Tureku");
        Assert.NotNull(humanProfile);
        Assert.False(humanProfile.IsBot);
        Assert.True(humanProfile.Edits >= 10_000);

        Assert.Null(await profileSource.ProfileAsync("DefinitelyNotAnAccountInFiWiki_98"));
    }
}
