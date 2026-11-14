namespace Junction.Web.Features.Analysis;

/// <summary>
/// <see cref="IAnalysisModel"/> over Gemini: prompt → cache → budget → concurrency limit → transport (with retries) →
/// schema validation → one repair retry → cache + ledger. Implemented by the orchestrator once the parts land.
/// </summary>
public sealed class GeminiAnalysisModel : IAnalysisModel
{
    public Task<AnalysisResult<T>> RunAsync<T>(AnalysisRequest request, CancellationToken ct = default) =>
        throw new NotImplementedException("analysis pipeline");
}
