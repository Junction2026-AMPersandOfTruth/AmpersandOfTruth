using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using AmpersandOfTruth.Tests.Infrastructure;
using AmpersandOfTruth.Web.Data;
using AmpersandOfTruth.Web.Features.Evidence;
using AmpersandOfTruth.Web.Features.Investigations;
using AmpersandOfTruth.Web.Features.Library;
using AmpersandOfTruth.Web.Features.Sharing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace AmpersandOfTruth.Tests.Features.Sharing;

public sealed class ShareLinkStoreTests
{
    [Fact]
    public async Task Create_generates_43_char_token_stores_only_sha256_hash_and_resolves_to_investigation()
    {
        using var db = new TestDb();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 14, 10, 0, 0, TimeSpan.Zero));
        var library = new LibraryStore(db.Factory, clock);
        var shares = new ShareLinkStore(db.Factory, clock);

        var id = await library.SaveAsync("owner-alice", "Winter War", MakeSnapshot(clock.GetUtcNow()));

        var token = await shares.CreateAsync(id, "owner-alice", lifetime: null);
        Assert.NotNull(token);
        Assert.Equal(43, token.Length);

        var rawBytes = Base64Url.DecodeFromChars(token);
        Assert.Equal(32, rawBytes.Length);

        // Plain token is never stored in SQLite; only its 64-char lowercase SHA-256 hex hash is stored.
        await using (var ctx = db.CreateContext())
        {
            var stored = await ctx.Set<ShareLink>().AsNoTracking().SingleAsync();
            var expectedHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

            Assert.Equal(64, stored.TokenHash.Length);
            Assert.Equal(expectedHash, stored.TokenHash);
            Assert.NotEqual(token, stored.TokenHash);
            Assert.DoesNotContain(token, stored.TokenHash, StringComparison.Ordinal);
            Assert.Equal(id, stored.InvestigationId);
            Assert.Equal(clock.GetUtcNow(), stored.CreatedAt);
            Assert.Null(stored.ExpiresAt);
            Assert.Null(stored.RevokedAt);
        }

        var resolved = await shares.ResolveAsync(token);
        Assert.Equal(id, resolved);

        // Unknown well-formed 43-char token resolves to null.
        var unknownWellFormed = new string('A', 43);
        Assert.Null(await shares.ResolveAsync(unknownWellFormed));
    }

    [Fact]
    public async Task Wrong_owner_cannot_create_list_or_revoke_links()
    {
        using var db = new TestDb();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 14, 11, 0, 0, TimeSpan.Zero));
        var library = new LibraryStore(db.Factory, clock);
        var shares = new ShareLinkStore(db.Factory, clock);

        var id = await library.SaveAsync("owner-alice", "Winter War", MakeSnapshot(clock.GetUtcNow()));

        // Wrong owner and unknown investigation cannot create
        Assert.Null(await shares.CreateAsync(id, "owner-mallory", lifetime: null));
        Assert.Null(await shares.CreateAsync(id, "", lifetime: null));
        Assert.Null(await shares.CreateAsync(Guid.NewGuid(), "owner-alice", lifetime: null));

        var token = await shares.CreateAsync(id, "owner-alice", TimeSpan.FromDays(7));
        Assert.NotNull(token);

        var aliceLinks = await shares.ForInvestigationAsync(id, "owner-alice");
        var linkInfo = Assert.Single(aliceLinks);
        Assert.Equal(8, linkInfo.TokenHashPrefix.Length);
        Assert.Equal(ShareLinkStore.HashToken(token)[..8], linkInfo.TokenHashPrefix);

        // Wrong owner cannot list
        Assert.Empty(await shares.ForInvestigationAsync(id, "owner-mallory"));
        Assert.Empty(await shares.ForInvestigationAsync(Guid.NewGuid(), "owner-alice"));

        // Wrong owner cannot revoke
        Assert.False(await shares.RevokeAsync(id, "owner-mallory", linkInfo.TokenHashPrefix));
        Assert.Equal(id, await shares.ResolveAsync(token));
    }

    [Fact]
    public async Task Revoke_marks_link_revoked_and_resolve_returns_null()
    {
        using var db = new TestDb();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 14, 12, 0, 0, TimeSpan.Zero));
        var library = new LibraryStore(db.Factory, clock);
        var shares = new ShareLinkStore(db.Factory, clock);

        var id = await library.SaveAsync("owner-1", "Continuation War", MakeSnapshot(clock.GetUtcNow()));
        var token = await shares.CreateAsync(id, "owner-1", lifetime: null);
        Assert.NotNull(token);

        var info = Assert.Single(await shares.ForInvestigationAsync(id, "owner-1"));

        clock.Advance(TimeSpan.FromMinutes(10));
        Assert.True(await shares.RevokeAsync(id, "owner-1", info.TokenHashPrefix));

        // Second revoke returns false
        Assert.False(await shares.RevokeAsync(id, "owner-1", info.TokenHashPrefix));

        // Resolve returns null and active list is empty
        Assert.Null(await shares.ResolveAsync(token));
        Assert.Empty(await shares.ForInvestigationAsync(id, "owner-1"));

        await using var ctx = db.CreateContext();
        var stored = await ctx.Set<ShareLink>().AsNoTracking().SingleAsync();
        Assert.Equal(clock.GetUtcNow(), stored.RevokedAt);
    }

    [Fact]
    public async Task Expired_link_resolves_to_null_while_never_expiring_link_stays_valid()
    {
        using var db = new TestDb();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 14, 12, 0, 0, TimeSpan.Zero));
        var library = new LibraryStore(db.Factory, clock);
        var shares = new ShareLinkStore(db.Factory, clock);

        var id = await library.SaveAsync("owner-1", "Expiry test", MakeSnapshot(clock.GetUtcNow()));
        var sevenDayToken = await shares.CreateAsync(id, "owner-1", TimeSpan.FromDays(7));
        var thirtyDayToken = await shares.CreateAsync(id, "owner-1", TimeSpan.FromDays(30));
        var neverToken = await shares.CreateAsync(id, "owner-1", lifetime: null);

        Assert.NotNull(sevenDayToken);
        Assert.NotNull(thirtyDayToken);
        Assert.NotNull(neverToken);

        // Just before 7 days -> all three still valid
        clock.Advance(TimeSpan.FromDays(7) - TimeSpan.FromSeconds(1));
        Assert.Equal(id, await shares.ResolveAsync(sevenDayToken));
        Assert.Equal(id, await shares.ResolveAsync(thirtyDayToken));
        Assert.Equal(id, await shares.ResolveAsync(neverToken));

        // At 7 days -> 7-day token expires, 30-day and never still valid
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Null(await shares.ResolveAsync(sevenDayToken));
        Assert.Equal(id, await shares.ResolveAsync(thirtyDayToken));
        Assert.Equal(id, await shares.ResolveAsync(neverToken));

        // At 30 days -> 30-day token expires, never still valid even after 2 years
        clock.Advance(TimeSpan.FromDays(23));
        Assert.Null(await shares.ResolveAsync(thirtyDayToken));
        clock.Advance(TimeSpan.FromDays(730));
        Assert.Equal(id, await shares.ResolveAsync(neverToken));
    }

    [Theory]
    [InlineData("")]
    [InlineData("doesnotexist")]
    [InlineData("junk")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")] // 42 chars
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")] // 44 chars
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=")] // '=' padding not valid in Base64Url
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA+")] // '+' not valid in Base64Url
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA/")] // '/' not valid in Base64Url
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA!")] // non-Base64Url char
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA ")] // trailing space
    public async Task Malformed_token_returns_null_without_touching_database(string badToken)
    {
        var throwingFactory = new ThrowingDbContextFactory();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 14, 12, 0, 0, TimeSpan.Zero));
        var shares = new ShareLinkStore(throwingFactory, clock);

        var resolved = await shares.ResolveAsync(badToken);

        Assert.Null(resolved);
        Assert.Equal(0, throwingFactory.CallCount);
    }

    [Fact]
    public async Task Deleting_saved_investigation_cascades_share_links()
    {
        using var db = new TestDb();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 14, 13, 0, 0, TimeSpan.Zero));
        var library = new LibraryStore(db.Factory, clock);
        var shares = new ShareLinkStore(db.Factory, clock);

        var id = await library.SaveAsync("owner-1", "Cascade check", MakeSnapshot(clock.GetUtcNow()));
        var token1 = await shares.CreateAsync(id, "owner-1", lifetime: null);
        var token2 = await shares.CreateAsync(id, "owner-1", TimeSpan.FromDays(30));
        Assert.NotNull(token1);
        Assert.NotNull(token2);

        await using (var beforeCtx = db.CreateContext())
        {
            Assert.Equal(2, await beforeCtx.Set<ShareLink>().CountAsync());
        }

        Assert.True(await library.DeleteAsync(id, "owner-1"));

        Assert.Null(await shares.ResolveAsync(token1));
        Assert.Null(await shares.ResolveAsync(token2));

        await using var afterCtx = db.CreateContext();
        Assert.Equal(0, await afterCtx.Set<ShareLink>().CountAsync());
    }

    private static SavedSnapshot MakeSnapshot(DateTimeOffset analysedAt) =>
        new(
            "Winter War",
            [new TopicArticle("fi", 1150, "Talvisota", 0.04)],
            [
                new IncidentRecord
                {
                    Id = "inc-1",
                    PageId = 1150,
                    InsertingRevIds = [1001],
                    RemovingRevIds = [1002],
                    Tier = EvidenceTier.Contested,
                    Score = 60,
                    ClaimEn = "Sample claim",
                    ClaimFi = "Esimerkkiväite",
                },
            ],
            analysedAt);

    private sealed class ThrowingDbContextFactory : IDbContextFactory<AppDbContext>
    {
        public int CallCount { get; private set; }

        public AppDbContext CreateDbContext()
        {
            CallCount++;
            throw new InvalidOperationException("Database must not be accessed for malformed tokens.");
        }

        public Task<AppDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        {
            CallCount++;
            throw new InvalidOperationException("Database must not be accessed for malformed tokens.");
        }
    }
}
