using System.Collections.Concurrent;
using System.Globalization;
using AmpersandOfTruth.Web.Features.Corpus;
using AmpersandOfTruth.Web.Features.Evidence;
using AmpersandOfTruth.Web.Features.Investigations;
using MySqlConnector;

namespace AmpersandOfTruth.Web.Features.Accounts;

/// <summary>
/// Observable corpus-scoped activity facts for a named Wikipedia account from <c>research.s6_editor_profile</c>.
/// </summary>
public sealed record EditorProfile(
    string User,
    bool IsBot,
    DateTime? FirstTs,
    DateTime? LastTs,
    int Edits,
    int NonNullEdits,
    int ActiveDays,
    long? TopPageId,
    string? TopPageTitle,
    double TopPageShare,
    int RevertedEditsByOthers,
    int RevertsMadeOthers,
    double CustomSummaryUsage);

/// <summary>
/// Optional lookup over <c>research.s6_editor_profile</c> for named Wikipedia accounts.
/// Returns <c>null</c> when the account or optional experiment table is absent.
/// </summary>
public interface IEditorProfileSource
{
    Task<EditorProfile?> ProfileAsync(string user, CancellationToken ct = default);
}

/// <summary>
/// In-memory 1-hour cache over <see cref="IEditorProfileSource"/>. Singleton, thread-safe.
/// Failures are not cached so retrying after corpus recovery succeeds immediately.
/// </summary>
public sealed class CachedEditorProfileSource(IEditorProfileSource inner, TimeProvider clock) : IEditorProfileSource
{
    public static readonly TimeSpan Ttl = TimeSpan.FromHours(1);
    private const int MaxEntries = 5_000;

    private readonly ConcurrentDictionary<string, (EditorProfile? Profile, DateTimeOffset ExpiresAt)> _cache =
        new(StringComparer.Ordinal);

    public async Task<EditorProfile?> ProfileAsync(string user, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(user);
        var key = user.Trim();
        var now = clock.GetUtcNow();

        if (_cache.TryGetValue(key, out var hit) && hit.ExpiresAt > now)
            return hit.Profile;

        var profile = await inner.ProfileAsync(key, ct);
        if (_cache.Count >= MaxEntries)
            _cache.Clear();

        _cache[key] = (profile, clock.GetUtcNow() + Ttl);
        return profile;
    }
}

/// <summary>
/// Raw SQL against <c>research.s6_editor_profile</c> joined with <c>corpus.article</c> (<c>lang = 'fi'</c>).
/// </summary>
public sealed class MariaDbEditorProfileSource(CorpusConnectionFactory corpus, ILogger<MariaDbEditorProfileSource> log)
    : IEditorProfileSource
{
    internal const string ProfileSql = """
        SELECT ep.is_bot,
               ep.first_ts,
               ep.last_ts,
               ep.edits,
               ep.non_null_edits,
               ep.active_days,
               ep.top_page_id,
               a.title,
               ep.top_page_share,
               ep.reverted_edits_by_others,
               ep.reverts_made_others,
               ep.custom_summary_usage
        FROM research.s6_editor_profile ep
        LEFT JOIN article a ON a.lang = 'fi' AND a.page_id = ep.top_page_id
        WHERE ep.`user` = @name AND ep.kind = 'named'
        """;

    public async Task<EditorProfile?> ProfileAsync(string user, CancellationToken ct = default)
    {
        var label = EditorDisplay.For(user);
        if (label.IsAnonymous || label.Kind != EditorKind.Named)
            return null;

        var account = label.DisplayName;

        try
        {
            await using var conn = await corpus.OpenAsync(ct);
            await using var cmd = new MySqlCommand(ProfileSql, conn);
            cmd.Parameters.AddWithValue("@name", account);

            await using var r = await cmd.ExecuteReaderAsync(ct);
            if (!await r.ReadAsync(ct))
                return null;

            return new EditorProfile(
                User: account,
                IsBot: Convert.ToBoolean(r.GetValue(0), CultureInfo.InvariantCulture),
                FirstTs: DateTime.SpecifyKind(r.GetDateTime(1), DateTimeKind.Utc),
                LastTs: DateTime.SpecifyKind(r.GetDateTime(2), DateTimeKind.Utc),
                Edits: Convert.ToInt32(r.GetValue(3), CultureInfo.InvariantCulture),
                NonNullEdits: Convert.ToInt32(r.GetValue(4), CultureInfo.InvariantCulture),
                ActiveDays: Convert.ToInt32(r.GetValue(5), CultureInfo.InvariantCulture),
                TopPageId: r.IsDBNull(6) ? null : Convert.ToInt64(r.GetValue(6), CultureInfo.InvariantCulture),
                TopPageTitle: r.IsDBNull(7) ? null : r.GetString(7),
                TopPageShare: Convert.ToDouble(r.GetValue(8), CultureInfo.InvariantCulture),
                RevertedEditsByOthers: Convert.ToInt32(r.GetValue(9), CultureInfo.InvariantCulture),
                RevertsMadeOthers: Convert.ToInt32(r.GetValue(10), CultureInfo.InvariantCulture),
                CustomSummaryUsage: Convert.ToDouble(r.GetValue(11), CultureInfo.InvariantCulture));
        }
        catch (MySqlException ex) when (ex.ErrorCode is MySqlErrorCode.NoSuchTable
                                        or MySqlErrorCode.UnknownDatabase
                                        or MySqlErrorCode.TableAccessDenied)
        {
            log.LogDebug(ex, "Optional research.s6_editor_profile table unavailable on {Server}", corpus.Server);
            return null;
        }
        catch (Exception ex) when (ex is MySqlException or TimeoutException)
        {
            log.LogWarning(ex, "Editor profile query against {Server} failed", corpus.Server);
            throw new CorpusUnavailableException($"Corpus {corpus.Server} not available: {ex.Message}", ex);
        }
    }
}
