using System.Globalization;
using System.Text.Json;
using AmpersandOfTruth.Web.Features.WebSpread.Store;

namespace AmpersandOfTruth.Web.Features.WebSpread.Dating;

/// <summary>The Internet Archive's earliest capture of a URL (P3.5 source 2): an independent upper bound on when the page existed.</summary>
public interface IWebArchive
{
    /// <summary>The day of the earliest successful capture, or null (never archived, or the archive is unreachable).</summary>
    Task<DateOnly?> FirstCaptureAsync(string url, CancellationToken ct = default);
}

/// <summary>
/// Wayback Machine CDX API: <c>GET https://web.archive.org/cdx/search/cdx?url=…&amp;output=json&amp;limit=1&amp;fl=timestamp&amp;filter=statuscode:200</c>
/// → <c>[["timestamp"],["20140305123456"]]</c>. Free, no key; results cached in <c>WebSpreadCache</c> (kind <c>archive</c>),
/// misses included, so a URL is asked once. Unreachable → null, never an exception.
/// TODO(P3.5): fetch the snapshot nearest the exposure window and check the claim is in it (<see cref="DateMethod.ArchiveWithClaim"/>);
/// verify the CDX usage rules and that web.archive.org is allowed by the environment's network policy.
/// </summary>
public sealed class WaybackArchive(HttpClient http, WebSpreadStore store, TimeProvider time, ILogger<WaybackArchive> log) : IWebArchive
{
    public const string CdxUrl = "https://web.archive.org/cdx/search/cdx";

    public async Task<DateOnly?> FirstCaptureAsync(string url, CancellationToken ct = default)
    {
        var key = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes("archive\u001f" + UrlPolicy.Normalize(url))));
        if (await store.GetAsync(key, ct) is { } hit)
            return hit.ResponseJson == "null" ? null : DateOnly.ParseExact(hit.ResponseJson.Trim('"'), "yyyy-MM-dd", CultureInfo.InvariantCulture);

        DateOnly? day;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20), time);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
            var query = $"{CdxUrl}?url={Uri.EscapeDataString(url)}&output=json&limit=1&fl=timestamp&filter=statuscode:200";
            using var response = await http.GetAsync(query, linked.Token);
            if (!response.IsSuccessStatusCode)
            {
                log.LogInformation("Wayback CDX returned {Status} for {Url}", (int)response.StatusCode, url);
                return null; // transient: not cached
            }
            day = ParseCdx(await response.Content.ReadAsStringAsync(linked.Token));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException)
        {
            log.LogInformation("Wayback CDX unavailable for {Url}: {Error}", url, ex.Message);
            return null;
        }

        await store.PutAsync(new WebCacheEntry
        {
            Key = key, Kind = "archive", Request = url.Length > 2000 ? url[..2000] : url,
            ResponseJson = day is null ? "null" : $"\"{day:yyyy-MM-dd}\"", CreatedAt = time.GetUtcNow(),
        }, ct);
        return day;
    }

    /// <summary>Parses the CDX JSON (a header row, then rows). Empty body or no rows = never archived.</summary>
    public static DateOnly? ParseCdx(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        using var doc = JsonDocument.Parse(body);
        if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() < 2) return null;
        var ts = doc.RootElement[1][0].GetString();
        return ts is { Length: >= 8 } && DateOnly.TryParseExact(ts[..8], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d : null;
    }
}
