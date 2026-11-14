using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using AmpersandOfTruth.Web.Data;
using AmpersandOfTruth.Web.Features.Library;
using Microsoft.EntityFrameworkCore;

namespace AmpersandOfTruth.Web.Features.Sharing;

public sealed class ShareLinkStore(IDbContextFactory<AppDbContext> dbFactory, TimeProvider time)
{
    public const int TokenByteLength = 32;
    public const int TokenCharLength = 43;
    public const int TokenHashLength = 64;
    public const int PrefixLength = 8;
    private const int MaxOwnerKeyLength = 64;

    public async Task<string?> CreateAsync(
        Guid investigationId,
        string ownerKey,
        TimeSpan? lifetime,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(ownerKey))
        {
            return null;
        }

        var normalizedOwner = Clamp(ownerKey.Trim(), MaxOwnerKeyLength);

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var owns = await db.Set<SavedInvestigation>()
            .AsNoTracking()
            .AnyAsync(x => x.Id == investigationId && x.OwnerKey == normalizedOwner, ct);

        if (!owns)
        {
            return null;
        }

        var token = GenerateToken();
        var hash = HashToken(token);
        var now = time.GetUtcNow();
        DateTimeOffset? expiresAt = lifetime is { } lt ? now.Add(lt) : null;

        db.Set<ShareLink>().Add(new ShareLink
        {
            TokenHash = hash,
            InvestigationId = investigationId,
            CreatedAt = now,
            ExpiresAt = expiresAt,
            RevokedAt = null,
        });

        await db.SaveChangesAsync(ct);
        return token;
    }

    public async Task<Guid?> ResolveAsync(string token, CancellationToken ct = default)
    {
        if (!IsWellFormedToken(token))
        {
            return null;
        }

        var hash = HashToken(token);
        var now = time.GetUtcNow();

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var link = await db.Set<ShareLink>()
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.TokenHash == hash, ct);

        if (link is null || link.RevokedAt is not null)
        {
            return null;
        }

        if (link.ExpiresAt is { } expiresAt && expiresAt <= now)
        {
            return null;
        }

        return link.InvestigationId;
    }

    public async Task<IReadOnlyList<ShareLinkInfo>> ForInvestigationAsync(
        Guid investigationId,
        string ownerKey,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(ownerKey))
        {
            return [];
        }

        var normalizedOwner = Clamp(ownerKey.Trim(), MaxOwnerKeyLength);
        var now = time.GetUtcNow();

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var owns = await db.Set<SavedInvestigation>()
            .AsNoTracking()
            .AnyAsync(x => x.Id == investigationId && x.OwnerKey == normalizedOwner, ct);

        if (!owns)
        {
            return [];
        }

        var rows = await db.Set<ShareLink>()
            .AsNoTracking()
            .Where(x => x.InvestigationId == investigationId && x.RevokedAt == null)
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync(ct);

        var result = new List<ShareLinkInfo>(rows.Count);
        foreach (var row in rows)
        {
            if (row.ExpiresAt is { } expiresAt && expiresAt <= now)
            {
                continue;
            }

            var prefix = row.TokenHash.Length >= PrefixLength ? row.TokenHash[..PrefixLength] : row.TokenHash;
            result.Add(new ShareLinkInfo(prefix, row.CreatedAt, row.ExpiresAt));
        }

        return result;
    }

    public async Task<bool> RevokeAsync(
        Guid investigationId,
        string ownerKey,
        string tokenHashPrefix,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(ownerKey) || string.IsNullOrWhiteSpace(tokenHashPrefix))
        {
            return false;
        }

        var prefix = tokenHashPrefix.Trim().ToLowerInvariant();
        if (prefix.Length is < PrefixLength or > TokenHashLength || !IsLowerHex(prefix))
        {
            return false;
        }

        var normalizedOwner = Clamp(ownerKey.Trim(), MaxOwnerKeyLength);

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var owns = await db.Set<SavedInvestigation>()
            .AsNoTracking()
            .AnyAsync(x => x.Id == investigationId && x.OwnerKey == normalizedOwner, ct);

        if (!owns)
        {
            return false;
        }

        var now = time.GetUtcNow();
        var updated = await db.Set<ShareLink>()
            .Where(x => x.InvestigationId == investigationId
                && x.RevokedAt == null
                && x.TokenHash.StartsWith(prefix))
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, now), ct);

        return updated > 0;
    }

    public static string GenerateToken()
    {
        Span<byte> bytes = stackalloc byte[TokenByteLength];
        RandomNumberGenerator.Fill(bytes);
        return Base64Url.EncodeToString(bytes);
    }

    public static string HashToken(string token) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    public static bool IsWellFormedToken(string? token)
    {
        if (token is null || token.Length != TokenCharLength)
        {
            return false;
        }

        for (var i = 0; i < token.Length; i++)
        {
            var ch = token[i];
            var isUrlSafe = (ch >= 'A' && ch <= 'Z')
                || (ch >= 'a' && ch <= 'z')
                || (ch >= '0' && ch <= '9')
                || ch is '-' or '_';
            if (!isUrlSafe)
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsLowerHex(string s)
    {
        for (var i = 0; i < s.Length; i++)
        {
            var ch = s[i];
            if (!((ch >= '0' && ch <= '9') || (ch >= 'a' && ch <= 'f')))
            {
                return false;
            }
        }

        return true;
    }

    private static string Clamp(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];
}
