using AmpersandOfTruth.Web.Features.Analysis;
using AmpersandOfTruth.Web.Features.Analysis.Store;

namespace AmpersandOfTruth.Tests.Features.Analysis.Store;

public sealed class ModelPricingTests
{
    [Fact]
    public void EstimateUsd_computes_exact_hydrogen_price_and_is_case_insensitive()
    {
        var options = new GeminiOptions
        {
            // Deliberately use a default case-sensitive Dictionary to prove lookup is still case-insensitive.
            Pricing = new Dictionary<string, ModelPrice>
            {
                ["gemini-4-hydrogen"] = new()
                {
                    InputPerMTok = 1.5,
                    OutputPerMTok = 7.5,
                    CachedInputPerMTok = 0.075,
                },
            },
        };
        var pricing = new ModelPricing(new TestOptionsMonitor<GeminiOptions>(options));

        var usage = new AnalysisUsage(
            InputTokens: 1_000_000,
            OutputTokens: 100_000,
            ThoughtTokens: 50_000,
            CachedTokens: 200_000);

        // (800,000 * 1.5 + 200,000 * 0.075 + 150,000 * 7.5) / 1,000,000
        // = (1,200,000 + 15,000 + 1,125,000) / 1,000,000 = 2.34
        Assert.Equal(2.34, pricing.EstimateUsd("gemini-4-hydrogen", usage), precision: 12);
        Assert.Equal(2.34, pricing.EstimateUsd("GEMINI-4-HYDROGEN", usage), precision: 12);
    }

    [Fact]
    public void EstimateUsd_returns_zero_for_unknown_model_and_clamps_negative_uncached_input()
    {
        var options = new GeminiOptions
        {
            Pricing =
            {
                ["gemini-4-hydrogen"] = new()
                {
                    InputPerMTok = 1.5,
                    OutputPerMTok = 7.5,
                    CachedInputPerMTok = 0.075,
                },
            },
        };
        var pricing = new ModelPricing(new TestOptionsMonitor<GeminiOptions>(options));

        var usage = new AnalysisUsage(
            InputTokens: 100_000,
            OutputTokens: 10_000,
            ThoughtTokens: 0,
            CachedTokens: 200_000);

        Assert.Equal(0.0, pricing.EstimateUsd("unknown-model", usage));

        // Uncached input clamps to 0: (0 * 1.5 + 200,000 * 0.075 + 10,000 * 7.5) / 1,000,000 = 0.09
        Assert.Equal(0.09, pricing.EstimateUsd("gemini-4-hydrogen", usage), precision: 12);
    }
}
