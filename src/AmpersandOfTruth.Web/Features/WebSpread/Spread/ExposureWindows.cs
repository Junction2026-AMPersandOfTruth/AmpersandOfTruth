using AmpersandOfTruth.Web.Features.Investigations;

namespace AmpersandOfTruth.Web.Features.WebSpread.Spread;

/// <summary>One stretch of time the claim was live on Wikipedia. <see cref="Until"/> null = still live (never removed).</summary>
public sealed record ExposureWindow(DateTimeOffset From, DateTimeOffset? Until);

/// <summary>
/// The incident's exposure windows (P1.S3 input to P3.6): inserted → removed, re-inserted → removed again, …
/// Built from <see cref="IncidentRecord.Edits"/> (in time order since #120); falls back to
/// <see cref="IncidentRecord.FirstInsertedAt"/>/<see cref="IncidentRecord.LastRemovedAt"/> when edits carry no timestamps.
/// </summary>
public static class ExposureWindows
{
    public static IReadOnlyList<ExposureWindow> From(IncidentRecord incident)
    {
        var windows = new List<ExposureWindow>();
        DateTimeOffset? open = null;
        foreach (var e in incident.Edits.Where(e => e.Timestamp is not null).OrderBy(e => e.Timestamp))
        {
            var at = e.Timestamp!.Value;
            if (e.Role is IncidentEditRole.Inserted or IncidentEditRole.Reinserted)
            {
                open ??= at;
            }
            else if (e.Role == IncidentEditRole.Removed && open is not null)
            {
                windows.Add(new ExposureWindow(open.Value, at));
                open = null;
            }
        }
        if (open is not null) windows.Add(new ExposureWindow(open.Value, null));

        if (windows.Count == 0 && incident.FirstInsertedAt is { } first)
            windows.Add(new ExposureWindow(first, incident.LastRemovedAt));
        return windows;
    }

    /// <summary>The three search windows of P3.2, in UTC days: before the first insertion, while live (first insertion to
    /// last removal), after the last removal. "During" and "after" collapse when the claim was never removed.</summary>
    public static IReadOnlyList<(SearchWindowKind Kind, DateWindow Window)> SearchWindows(IReadOnlyList<ExposureWindow> windows, DateOnly today)
    {
        if (windows.Count == 0) return [(SearchWindowKind.Any, new DateWindow(null, null))];
        var first = Day(windows[0].From);
        var lastUntil = windows[^1].Until is { } u ? Day(u) : (DateOnly?)null;

        var result = new List<(SearchWindowKind, DateWindow)>
        {
            (SearchWindowKind.Before, new DateWindow(null, first.AddDays(-1))),
            (SearchWindowKind.During, new DateWindow(first, lastUntil ?? today)),
        };
        if (lastUntil is { } end && end < today) result.Add((SearchWindowKind.After, new DateWindow(end.AddDays(1), today)));
        return result;
    }

    public static DateOnly Day(DateTimeOffset at) => DateOnly.FromDateTime(at.UtcDateTime);
}

public enum SearchWindowKind { Any, Before, During, After }
