namespace AmpersandOfTruth.Tests.Features.Corpus;

/// <summary>
/// Integration test against the real corpus. Skipped unless <c>CORPUS_TESTS=1</c>, so <c>verify.sh</c>
/// stays green without the tunnel. Run: <c>scripts/corpus-tunnel.sh &amp;&amp; CORPUS_TESTS=1 dotnet test --filter Category=Corpus</c>
/// (needs <c>Corpus:Password</c> in user-secrets or <c>Corpus__Password</c> in the environment).
/// </summary>
public sealed class CorpusFactAttribute : FactAttribute
{
    public CorpusFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("CORPUS_TESTS") != "1")
            Skip = "Corpus integration test — set CORPUS_TESTS=1 (and run scripts/corpus-tunnel.sh) to enable.";
    }
}
