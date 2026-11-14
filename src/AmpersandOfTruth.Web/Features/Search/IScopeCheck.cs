namespace AmpersandOfTruth.Web.Features.Search;

public sealed record ScopeResult(bool InScope, IReadOnlyList<string> Suggestions);

public sealed record OutOfScopeTopic(string Topic, IReadOnlyList<string> Suggestions);

public interface IScopeCheck
{
    Task<ScopeResult> CheckAsync(string topic, CancellationToken ct = default);
}

/// <summary>Placeholder until P1.R6 (#27) lands.</summary>
public sealed class AlwaysInScope : IScopeCheck
{
    private static readonly Task<ScopeResult> InScope =
        Task.FromResult(new ScopeResult(true, []));

    public Task<ScopeResult> CheckAsync(string topic, CancellationToken ct = default) => InScope;
}
