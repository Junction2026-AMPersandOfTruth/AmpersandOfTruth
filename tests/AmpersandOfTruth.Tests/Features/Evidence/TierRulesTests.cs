using AmpersandOfTruth.Web.Features.Evidence;

namespace AmpersandOfTruth.Tests.Features.Evidence;

public sealed class TierRulesTests
{
    private static IncidentEvidence CreateContestedEvidence() => new()
    {
        IsSubstantive = true,
        RemovedByOtherAccount = true,
        InsertedByBot = false,
        IsBlanking = false,
        KeptInLatestRevision = false,
        Assessment = new ContentAssessment(
            Category: ContentCategory.ContentDispute,
            Confidence: 4,
            HasVerifiedQuote: true),
        RemovalReason = RemovalReason.Neutrality,
        ReinsertionCount = 0,
        InEditWar = false,
        DisputeTemplateAdded = false,
        DistinctInsertingEditors = 1,
        DistinctArticles = 1,
        InsertionSpan = TimeSpan.Zero,
        SourceManipulation = false,
    };

    [Theory]
    [InlineData(ExclusionReason.NotSubstantive)]
    [InlineData(ExclusionReason.SelfRemoval)]
    [InlineData(ExclusionReason.Bot)]
    [InlineData(ExclusionReason.Blanking)]
    [InlineData(ExclusionReason.Kept)]
    [InlineData(ExclusionReason.Graffiti)]
    public void Assess_detects_each_of_the_six_exclusions(ExclusionReason expectedExclusion)
    {
        var evidence = expectedExclusion switch
        {
            ExclusionReason.NotSubstantive => CreateContestedEvidence() with { IsSubstantive = false },
            ExclusionReason.SelfRemoval => CreateContestedEvidence() with { RemovedByOtherAccount = false },
            ExclusionReason.Bot => CreateContestedEvidence() with { InsertedByBot = true },
            ExclusionReason.Blanking => CreateContestedEvidence() with { IsBlanking = true },
            ExclusionReason.Kept => CreateContestedEvidence() with { KeptInLatestRevision = true },
            ExclusionReason.Graffiti => CreateContestedEvidence() with
            {
                Assessment = new ContentAssessment(ContentCategory.Vandalism, 3, false),
            },
            _ => throw new ArgumentOutOfRangeException(nameof(expectedExclusion)),
        };

        var result = TierRules.Assess(evidence);

        Assert.False(result.IsIncident);
        Assert.Null(result.Tier);
        Assert.Equal(expectedExclusion, result.Exclusion);
        Assert.Empty(result.Reasons);
        Assert.Null(result.BecauseSentence);
    }

    [Fact]
    public void Assess_excludes_graffiti_for_test_or_nonsense_category()
    {
        var evidence = CreateContestedEvidence() with
        {
            Assessment = new ContentAssessment(ContentCategory.TestOrNonsense, 4, false),
        };

        var result = TierRules.Assess(evidence);

        Assert.False(result.IsIncident);
        Assert.Null(result.Tier);
        Assert.Equal(ExclusionReason.Graffiti, result.Exclusion);
        Assert.Null(result.BecauseSentence);
    }

    [Fact]
    public void Assess_respects_exclusion_order_first_match_wins()
    {
        // NotSubstantive beats Bot
        var notSubstantiveAndBot = CreateContestedEvidence() with
        {
            IsSubstantive = false,
            InsertedByBot = true,
        };
        Assert.Equal(ExclusionReason.NotSubstantive, TierRules.Assess(notSubstantiveAndBot).Exclusion);

        // SelfRemoval beats Blanking
        var selfRemovalAndBlanking = CreateContestedEvidence() with
        {
            RemovedByOtherAccount = false,
            IsBlanking = true,
        };
        Assert.Equal(ExclusionReason.SelfRemoval, TierRules.Assess(selfRemovalAndBlanking).Exclusion);

        // Bot beats Blanking
        var botAndBlanking = CreateContestedEvidence() with
        {
            InsertedByBot = true,
            IsBlanking = true,
        };
        Assert.Equal(ExclusionReason.Bot, TierRules.Assess(botAndBlanking).Exclusion);

        // Blanking beats Kept
        var blankingAndKept = CreateContestedEvidence() with
        {
            IsBlanking = true,
            KeptInLatestRevision = true,
        };
        Assert.Equal(ExclusionReason.Blanking, TierRules.Assess(blankingAndKept).Exclusion);

        // Kept beats Graffiti
        var keptAndGraffiti = CreateContestedEvidence() with
        {
            KeptInLatestRevision = true,
            Assessment = new ContentAssessment(ContentCategory.Vandalism, 5, true),
        };
        Assert.Equal(ExclusionReason.Kept, TierRules.Assess(keptAndGraffiti).Exclusion);
    }

    [Theory]
    [InlineData(ContentCategory.Vandalism)]
    [InlineData(ContentCategory.TestOrNonsense)]
    public void Assess_low_confidence_vandalism_is_not_excluded_and_stays_reverted(ContentCategory category)
    {
        var evidence = CreateContestedEvidence() with
        {
            Assessment = new ContentAssessment(category, Confidence: 2, HasVerifiedQuote: true),
        };

        var result = TierRules.Assess(evidence);

        Assert.True(result.IsIncident);
        Assert.Equal(EvidenceTier.Reverted, result.Tier);
        Assert.Null(result.Exclusion);
        var reason = Assert.Single(result.Reasons);
        Assert.Equal(TierReasonKind.RemovedByOthers, reason.Kind);
        Assert.Equal("taken out by another editor", reason.Text);
    }

    [Fact]
    public void Assess_missing_assessment_never_promotes_reverts_even_with_all_other_signals()
    {
        var evidence = CreateContestedEvidence() with
        {
            Assessment = null,
            RemovalReason = RemovalReason.Accuracy,
            ReinsertionCount = 5,
            InEditWar = true,
            DisputeTemplateAdded = true,
            DistinctInsertingEditors = 4,
            DistinctArticles = 3,
            InsertionSpan = TimeSpan.FromDays(20),
            SourceManipulation = true,
        };

        var result = TierRules.Assess(evidence);

        Assert.True(result.IsIncident);
        Assert.Equal(EvidenceTier.Reverted, result.Tier);
        Assert.Null(result.Exclusion);
        var reason = Assert.Single(result.Reasons);
        Assert.Equal(TierReasonKind.RemovedByOthers, reason.Kind);
    }

    [Theory]
    [InlineData(ContentCategory.GoodFaithError, 4, true)]
    [InlineData(ContentCategory.Other, 4, true)]
    [InlineData(ContentCategory.ContentDispute, 4, false)]
    [InlineData(ContentCategory.ContentDispute, 2, true)]
    public void Assess_non_claim_categories_or_missing_quote_or_low_confidence_stay_reverted(
        ContentCategory category,
        int confidence,
        bool hasVerifiedQuote)
    {
        var evidence = CreateContestedEvidence() with
        {
            Assessment = new ContentAssessment(category, confidence, hasVerifiedQuote),
            RemovalReason = RemovalReason.Neutrality,
            ReinsertionCount = 1,
            InEditWar = true,
        };

        var result = TierRules.Assess(evidence);

        Assert.Equal(EvidenceTier.Reverted, result.Tier);
        var reason = Assert.Single(result.Reasons);
        Assert.Equal(TierReasonKind.RemovedByOthers, reason.Kind);
    }

    [Fact]
    public void Assess_claim_without_dispute_signal_is_reverted()
    {
        var evidence = CreateContestedEvidence() with
        {
            RemovalReason = RemovalReason.None,
            ReinsertionCount = 0,
            InEditWar = false,
            DisputeTemplateAdded = false,
        };

        var result = TierRules.Assess(evidence);

        Assert.Equal(EvidenceTier.Reverted, result.Tier);
        var reason = Assert.Single(result.Reasons);
        Assert.Equal(TierReasonKind.RemovedByOthers, reason.Kind);
    }

    [Theory]
    [InlineData(RemovalReason.Other)]
    [InlineData(RemovalReason.Vandalism)]
    public void Assess_non_dispute_removal_reasons_do_not_count_as_dispute_signals(RemovalReason reason)
    {
        var evidence = CreateContestedEvidence() with
        {
            RemovalReason = reason,
            ReinsertionCount = 0,
            InEditWar = false,
            DisputeTemplateAdded = false,
        };

        var result = TierRules.Assess(evidence);

        Assert.Equal(EvidenceTier.Reverted, result.Tier);
        var r = Assert.Single(result.Reasons);
        Assert.Equal(TierReasonKind.RemovedByOthers, r.Kind);
    }

    [Theory]
    [InlineData(RemovalReason.Accuracy)]
    [InlineData(RemovalReason.Neutrality)]
    [InlineData(RemovalReason.Sourcing)]
    public void Assess_each_removal_reason_alone_with_claim_is_contested(RemovalReason removalReason)
    {
        var evidence = CreateContestedEvidence() with
        {
            RemovalReason = removalReason,
            ReinsertionCount = 0,
            InEditWar = false,
            DisputeTemplateAdded = false,
        };

        var result = TierRules.Assess(evidence);

        Assert.Equal(EvidenceTier.Contested, result.Tier);
        Assert.Equal(2, result.Reasons.Count);
        Assert.Equal(TierReasonKind.RemovalReason, result.Reasons[0].Kind);
        Assert.Equal(TierReasonKind.ClaimContent, result.Reasons[1].Kind);

        string expectedText = removalReason switch
        {
            RemovalReason.Accuracy => "removed as inaccurate",
            RemovalReason.Neutrality => "removed as not neutral",
            RemovalReason.Sourcing => "removed for lack of a reliable source",
            _ => throw new ArgumentOutOfRangeException(nameof(removalReason)),
        };
        Assert.Equal(expectedText, result.Reasons[0].Text);
    }

    [Fact]
    public void Assess_reinsertion_alone_with_claim_is_contested()
    {
        var evidence = CreateContestedEvidence() with
        {
            RemovalReason = RemovalReason.None,
            ReinsertionCount = 1,
            InEditWar = false,
            DisputeTemplateAdded = false,
        };

        var result = TierRules.Assess(evidence);

        Assert.Equal(EvidenceTier.Contested, result.Tier);
        Assert.Equal(2, result.Reasons.Count);
        Assert.Equal(TierReasonKind.Reinserted, result.Reasons[0].Kind);
        Assert.Equal("put back once after removal", result.Reasons[0].Text);
        Assert.Equal(TierReasonKind.ClaimContent, result.Reasons[1].Kind);
    }

    [Fact]
    public void Assess_edit_war_alone_with_claim_is_contested()
    {
        var evidence = CreateContestedEvidence() with
        {
            RemovalReason = RemovalReason.None,
            ReinsertionCount = 0,
            InEditWar = true,
            DisputeTemplateAdded = false,
        };

        var result = TierRules.Assess(evidence);

        Assert.Equal(EvidenceTier.Contested, result.Tier);
        Assert.Equal(2, result.Reasons.Count);
        Assert.Equal(TierReasonKind.EditWar, result.Reasons[0].Kind);
        Assert.Equal("part of a back-and-forth over this passage", result.Reasons[0].Text);
        Assert.Equal(TierReasonKind.ClaimContent, result.Reasons[1].Kind);
    }

    [Fact]
    public void Assess_dispute_template_alone_with_claim_is_contested()
    {
        var evidence = CreateContestedEvidence() with
        {
            RemovalReason = RemovalReason.None,
            ReinsertionCount = 0,
            InEditWar = false,
            DisputeTemplateAdded = true,
        };

        var result = TierRules.Assess(evidence);

        Assert.Equal(EvidenceTier.Contested, result.Tier);
        Assert.Equal(2, result.Reasons.Count);
        Assert.Equal(TierReasonKind.DisputeTemplate, result.Reasons[0].Kind);
        Assert.Equal("the article was marked as disputed", result.Reasons[0].Text);
        Assert.Equal(TierReasonKind.ClaimContent, result.Reasons[1].Kind);
    }

    [Fact]
    public void Assess_single_editor_and_single_article_persistent_stays_contested()
    {
        // One editor, one article, re-inserted 5× over 30 days + source manipulation → Contested (needs editors/articles).
        var evidence = CreateContestedEvidence() with
        {
            DistinctInsertingEditors = 1,
            DistinctArticles = 1,
            ReinsertionCount = 5,
            InsertionSpan = TimeSpan.FromDays(30),
            SourceManipulation = true,
        };

        var result = TierRules.Assess(evidence);

        Assert.Equal(EvidenceTier.Contested, result.Tier);
    }

    /// <summary>The pre-S10 campaign threshold. The rule's mechanics (which pairs count, which must be about accounts or
    /// articles) are tested at 2; the default is 3 since P1.S10 (#37).</summary>
    private static readonly TierThresholds TwoSignals = new() { MinCoordinationSignals = 2 };

    [Fact]
    public void Default_campaign_needs_three_coordination_signals()
    {
        Assert.Equal(3, TierThresholds.Default.MinCoordinationSignals);

        var twoSignals = CreateContestedEvidence() with { DistinctInsertingEditors = 2, DistinctArticles = 2 };
        Assert.Equal(EvidenceTier.Contested, TierRules.Assess(twoSignals).Tier);

        var threeSignals = twoSignals with { ReinsertionCount = 2, InsertionSpan = TimeSpan.FromDays(7) };
        var result = TierRules.Assess(threeSignals);
        Assert.Equal(EvidenceTier.Campaign, result.Tier);
        Assert.Contains(result.Reasons, r => r.Kind == TierReasonKind.Persistence);
    }

    [Fact]
    public void Assess_two_editors_plus_two_articles_is_campaign()
    {
        var evidence = CreateContestedEvidence() with
        {
            DistinctInsertingEditors = 2,
            DistinctArticles = 2,
        };

        var result = TierRules.Assess(evidence, TwoSignals);

        Assert.Equal(EvidenceTier.Campaign, result.Tier);
    }

    [Fact]
    public void Assess_two_editors_plus_persistence_is_campaign()
    {
        var evidence = CreateContestedEvidence() with
        {
            DistinctInsertingEditors = 2,
            DistinctArticles = 1,
            ReinsertionCount = 2,
            InsertionSpan = TimeSpan.FromDays(7),
        };

        var result = TierRules.Assess(evidence, TwoSignals);

        Assert.Equal(EvidenceTier.Campaign, result.Tier);
    }

    [Fact]
    public void Assess_one_editor_plus_three_articles_plus_source_manipulation_is_campaign()
    {
        var evidence = CreateContestedEvidence() with
        {
            DistinctInsertingEditors = 1,
            DistinctArticles = 3,
            SourceManipulation = true,
        };

        var result = TierRules.Assess(evidence, TwoSignals);

        Assert.Equal(EvidenceTier.Campaign, result.Tier);
    }

    [Fact]
    public void Assess_two_editors_only_is_contested_under_defaults()
    {
        var evidence = CreateContestedEvidence() with
        {
            DistinctInsertingEditors = 2,
            DistinctArticles = 1,
            SourceManipulation = false,
            ReinsertionCount = 0,
        };

        var result = TierRules.Assess(evidence);

        Assert.Equal(EvidenceTier.Contested, result.Tier);
    }

    [Fact]
    public void Assess_persistence_requires_both_reinsertion_count_and_span()
    {
        // 2 re-insertions over 6 days → span threshold (7 days) not met → no Persistence reason
        var shortSpan = CreateContestedEvidence() with
        {
            DistinctInsertingEditors = 2,
            DistinctArticles = 1,
            ReinsertionCount = 2,
            InsertionSpan = TimeSpan.FromDays(6),
        };

        var shortResult = TierRules.Assess(shortSpan);
        // Only 2 editors signal fired, persistence did not → count = 1 < 2 → stays Contested
        Assert.Equal(EvidenceTier.Contested, shortResult.Tier);
        Assert.DoesNotContain(shortResult.Reasons, r => r.Kind == TierReasonKind.Persistence);

        // 1 re-insertion over 10 days → count threshold (2) not met → no Persistence reason
        var lowCount = CreateContestedEvidence() with
        {
            DistinctInsertingEditors = 2,
            DistinctArticles = 1,
            ReinsertionCount = 1,
            InsertionSpan = TimeSpan.FromDays(10),
        };

        var lowCountResult = TierRules.Assess(lowCount);
        Assert.Equal(EvidenceTier.Contested, lowCountResult.Tier);
        Assert.DoesNotContain(lowCountResult.Reasons, r => r.Kind == TierReasonKind.Persistence);
    }

    [Fact]
    public void Assess_respects_custom_thresholds()
    {
        var custom = new TierThresholds
        {
            MinCoordinationSignals = 1,
        };

        // 2 editors only is normally Contested, but with MinCoordinationSignals = 1 becomes Campaign
        var evidence = CreateContestedEvidence() with
        {
            DistinctInsertingEditors = 2,
            DistinctArticles = 1,
        };

        var result = TierRules.Assess(evidence, custom);

        Assert.Equal(EvidenceTier.Campaign, result.Tier);
    }

    [Fact]
    public void Assess_because_sentence_is_null_for_excluded()
    {
        var evidence = CreateContestedEvidence() with { IsSubstantive = false };

        var result = TierRules.Assess(evidence);

        Assert.Null(result.Tier);
        Assert.Null(result.BecauseSentence);
    }

    [Fact]
    public void Assess_exact_because_sentence_for_reverted()
    {
        var evidence = CreateContestedEvidence() with
        {
            Assessment = null,
        };

        var result = TierRules.Assess(evidence);

        Assert.Equal(EvidenceTier.Reverted, result.Tier);
        Assert.Equal("Removed by Wikipedia editors \u2014 because: taken out by another editor.", result.BecauseSentence);
    }

    [Fact]
    public void Assess_exact_because_sentence_for_contested()
    {
        var evidence = CreateContestedEvidence() with
        {
            RemovalReason = RemovalReason.Neutrality,
            ReinsertionCount = 2,
            InEditWar = false,
            DisputeTemplateAdded = false,
        };

        var result = TierRules.Assess(evidence);

        Assert.Equal(EvidenceTier.Contested, result.Tier);
        const string expected = "Disputed and removed \u2014 because: removed as not neutral, put back 2 times after removal, automated assessment: a factual or narrative claim.";
        Assert.Equal(expected, result.BecauseSentence);
    }

    [Fact]
    public void Assess_exact_because_sentence_for_campaign_and_persistence_drops_reinserted()
    {
        var evidence = CreateContestedEvidence() with
        {
            DistinctInsertingEditors = 2,
            DistinctArticles = 2,
            ReinsertionCount = 3,
            InsertionSpan = TimeSpan.FromDays(10.5),
            RemovalReason = RemovalReason.Accuracy,
            InEditWar = false,
            DisputeTemplateAdded = false,
            SourceManipulation = false,
        };

        var result = TierRules.Assess(evidence);

        Assert.Equal(EvidenceTier.Campaign, result.Tier);

        // Must contain persistence reason
        Assert.Contains("put back 3 times over 10 days", result.BecauseSentence);

        // Must drop the dispute "put back 3 times after removal" reason
        Assert.DoesNotContain("put back 3 times after removal", result.BecauseSentence);

        const string expected = "Pattern consistent with a coordinated attempt \u2014 because: added by 2 different editors, added to 2 articles, put back 3 times over 10 days, removed as inaccurate, automated assessment: a factual or narrative claim.";
        Assert.Equal(expected, result.BecauseSentence);
    }

    [Fact]
    public void Assess_campaign_retains_reinserted_when_persistence_did_not_fire()
    {
        var evidence = CreateContestedEvidence() with
        {
            DistinctInsertingEditors = 2,
            DistinctArticles = 2,
            ReinsertionCount = 1,
            InsertionSpan = TimeSpan.FromDays(2),
            RemovalReason = RemovalReason.None,
        };

        var result = TierRules.Assess(evidence, TwoSignals);

        Assert.Equal(EvidenceTier.Campaign, result.Tier);
        Assert.Contains("put back once after removal", result.BecauseSentence);
    }

    [Theory]
    [InlineData(1, "put back once after removal")]
    [InlineData(2, "put back 2 times after removal")]
    [InlineData(5, "put back 5 times after removal")]
    public void Assess_reinserted_reason_text_formats_singular_and_plural(int count, string expectedText)
    {
        var evidence = CreateContestedEvidence() with
        {
            RemovalReason = RemovalReason.None,
            ReinsertionCount = count,
        };

        var result = TierRules.Assess(evidence);

        Assert.Equal(EvidenceTier.Contested, result.Tier);
        var reason = result.Reasons.First(r => r.Kind == TierReasonKind.Reinserted);
        Assert.Equal(expectedText, reason.Text);
    }

    [Theory]
    [InlineData(-1, 1, 1, 3)]
    [InlineData(0, 0, 1, 3)]
    [InlineData(0, 1, 0, 3)]
    [InlineData(0, 1, 1, 0)]
    [InlineData(0, 1, 1, 6)]
    public void Assess_throws_argument_out_of_range_on_invalid_inputs(
        int reinsertions,
        int editors,
        int articles,
        int confidence)
    {
        var evidence = CreateContestedEvidence() with
        {
            ReinsertionCount = reinsertions,
            DistinctInsertingEditors = editors,
            DistinctArticles = articles,
            Assessment = new ContentAssessment(ContentCategory.ContentDispute, confidence, true),
        };

        Assert.Throws<ArgumentOutOfRangeException>(() => TierRules.Assess(evidence));
    }

    [Fact]
    public void Assess_throws_argument_null_when_evidence_is_null()
    {
        Assert.Throws<ArgumentNullException>(() => TierRules.Assess(null!));
    }

    [Theory]
    [InlineData(ContentCategory.ContentDispute)]
    [InlineData(ContentCategory.PovPush)]
    [InlineData(ContentCategory.FalseFactualClaim)]
    [InlineData(ContentCategory.SourceManipulation)]
    public void Assess_each_claim_category_qualifies_as_claim(ContentCategory category)
    {
        var evidence = CreateContestedEvidence() with
        {
            Assessment = new ContentAssessment(category, 3, true),
            RemovalReason = RemovalReason.Accuracy,
        };

        var result = TierRules.Assess(evidence);

        Assert.Equal(EvidenceTier.Contested, result.Tier);
    }
}
