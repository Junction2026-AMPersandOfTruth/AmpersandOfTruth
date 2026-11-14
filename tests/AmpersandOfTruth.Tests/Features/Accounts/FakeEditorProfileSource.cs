using AmpersandOfTruth.Web.Features.Accounts;
using AmpersandOfTruth.Web.Features.Investigations;

namespace AmpersandOfTruth.Tests.Features.Accounts;

public sealed class FakeEditorProfileSource : IEditorProfileSource
{
    public bool Unavailable { get; set; }
    public Exception? Fault { get; set; }
    public int Calls { get; private set; }
    public List<string> QueriedUsers { get; } = [];
    public Dictionary<string, EditorProfile?> Profiles { get; } = new(StringComparer.Ordinal);

    public Task<EditorProfile?> ProfileAsync(string user, CancellationToken ct = default)
    {
        Calls++;
        QueriedUsers.Add(user);

        if (Fault is not null)
            throw Fault;

        if (Unavailable)
            throw new CorpusUnavailableException("fake profile source is down");

        return Task.FromResult(Profiles.GetValueOrDefault(user));
    }
}
