using AmpersandOfTruth.Web.Features.Accounts;
using AmpersandOfTruth.Web.Features.Investigations;

namespace AmpersandOfTruth.Tests.Features.Accounts;

public sealed class FakeAccountCorpus : IAccountCorpus
{
    public bool Unavailable { get; set; }
    public int Calls { get; private set; }
    public List<string> QueriedUsers { get; } = [];
    public Dictionary<string, AccountFacts> Accounts { get; } = new(StringComparer.Ordinal);

    public Task<AccountFacts> FactsAsync(string user, CancellationToken ct = default)
    {
        Calls++;
        QueriedUsers.Add(user);

        if (Unavailable)
            throw new CorpusUnavailableException("fake corpus is down");

        if (Accounts.TryGetValue(user, out var facts))
            return Task.FromResult(facts);

        return Task.FromResult(new AccountFacts(user, 0, 0, 0, null, null, [], []));
    }
}
