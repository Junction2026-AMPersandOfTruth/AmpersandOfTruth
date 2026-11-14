using AmpersandOfTruth.Web.Features.Demo;

namespace AmpersandOfTruth.Tests.Features.Demo;

public sealed class ClientIdentityTests
{
    [Fact]
    public void Unknown_ip_gives_each_circuit_its_own_key_never_one_shared_bucket()
    {
        var a = new ClientIdentity();
        var b = new ClientIdentity();

        Assert.Null(a.Key);
        Assert.NotEqual(a.EffectiveKey, b.EffectiveKey);
        Assert.Equal(a.EffectiveKey, a.EffectiveKey);   // stable within one circuit
    }

    [Fact]
    public void Known_ip_is_the_key()
    {
        var a = new ClientIdentity();
        a.Seed("203.0.113.7");
        Assert.Equal("203.0.113.7", a.EffectiveKey);
    }
}
