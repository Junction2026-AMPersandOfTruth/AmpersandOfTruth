using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Junction.Web.Features.Analysis;

/// <summary>
/// Config-only check (never calls Gemini — health probes must not spend money). Registered with failure status
/// <c>Degraded</c>: a missing key keeps <c>/health</c> at HTTP 200 and the app usable, analysis just reports Failed.
/// </summary>
public sealed class GeminiHealthCheck(IOptionsMonitor<GeminiOptions> options) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        var o = options.CurrentValue;
        var models = string.Join(", ", Enum.GetValues<AnalysisJob>().Select(j => $"{j}={o.ModelFor(j)}"));
        return Task.FromResult(string.IsNullOrWhiteSpace(o.ApiKey)
            ? new HealthCheckResult(context.Registration.FailureStatus,
                "Gemini:ApiKey not configured (dotnet user-secrets set \"Gemini:ApiKey\" \"…\" --project src/Junction.Web)")
            : HealthCheckResult.Healthy($"Gemini key configured · {models}"));
    }
}
