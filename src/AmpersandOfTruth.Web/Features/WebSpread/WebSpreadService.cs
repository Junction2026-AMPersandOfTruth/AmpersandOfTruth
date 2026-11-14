using System.Runtime.CompilerServices;
using AmpersandOfTruth.Web.Features.Demo;
using AmpersandOfTruth.Web.Features.WebSpread.Spread;
using AmpersandOfTruth.Web.Features.WebSpread.Store;
using Microsoft.Extensions.Options;

namespace AmpersandOfTruth.Web.Features.WebSpread;

/// <summary>What the incident page may offer (P3.7).</summary>
public enum WebSpreadAvailability
{
    /// <summary>No credentials and nothing stored: the section is not shown at all.</summary>
    Off,
    /// <summary>Show a stored result only (demo safe mode, or no credentials on this deployment). Never a live call.</summary>
    StoredOnly,
    /// <summary>Stored result if any, plus "Trace this claim on the web" / "Search again".</summary>
    Live,
}

/// <summary>
/// The UI's entry point to web spread: stored result per incident + a live trace that stores itself when clean.
/// Live traces only run on an explicit user action and never in demo safe mode. Singleton; thread-safe.
/// </summary>
public sealed class WebSpreadService(
    WebSpreadTracer tracer,
    WebSpreadResults results,
    IOptionsMonitor<OxylabsOptions> oxylabs,
    IOptionsMonitor<DemoOptions> demo,
    ILogger<WebSpreadService> log)
{
    public bool IsConfigured => oxylabs.CurrentValue.EffectiveMode != OxylabsMode.None;

    public bool CanTraceLive => IsConfigured && !demo.CurrentValue.SafeMode;

    public Task<WebSpreadResult?> StoredAsync(string incidentId, CancellationToken ct = default) => results.GetAsync(incidentId, ct);

    public WebSpreadAvailability Availability(bool hasStored) =>
        CanTraceLive ? WebSpreadAvailability.Live
        : hasStored ? WebSpreadAvailability.StoredOnly
        : WebSpreadAvailability.Off;

    /// <summary>Runs a live trace and stores a clean result. Throws <see cref="InvalidOperationException"/> when live traces
    /// are off (no credentials, or safe mode) — the UI never offers the action then.</summary>
    public async IAsyncEnumerable<WebSpreadEvent> TraceAsync(WebSpreadClaim claim, [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (!CanTraceLive) throw new InvalidOperationException("Live web-spread traces are off (no Oxylabs credentials, or demo safe mode).");

        await foreach (var e in tracer.TraceAsync(claim, ct))
        {
            if (e is WebSpreadCompleted done)
            {
                try
                {
                    var stored = await results.SaveAsync(done.Result, CancellationToken.None);
                    log.LogInformation("Web spread for {Incident}: {Status}, {Sources} sources, stored={Stored}",
                        claim.IncidentId, done.Result.Status, done.Result.Sources.Count, stored);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    log.LogWarning(ex, "Could not store the web-spread result for {Incident}", claim.IncidentId);
                }
            }
            yield return e;
        }
    }
}
