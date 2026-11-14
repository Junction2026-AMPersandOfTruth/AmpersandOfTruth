namespace AmpersandOfTruth.Web.Features.Analysis.Prompts;

/// <summary>Typed output of the <c>classify-edit</c> prompt (mirrors <c>classify-edit.schema.json</c>; a test keeps them in sync).
/// v1 is the P0.5 comparison shape; P1.S9 (#36) iterates on it.</summary>
public sealed record EditClassification(
    string WhatChangedEn,
    IReadOnlyList<string> Evidence,
    string? ClaimEn,
    string? ClaimFi,
    string? NarrativeEn,
    EditCategory Category,
    int Confidence,
    string? HistoricalNoteEn,
    string? UncertaintyNote);

public enum EditCategory
{
    Vandalism,
    TestOrNonsense,
    GoodFaithError,
    ContentDispute,
    PovPush,
    FalseFactualClaim,
    SourceManipulation,
    Other,
}

/// <summary>Variable names of <c>classify-edit</c>, for callers building requests.</summary>
public static class ClassifyEditPrompt
{
    public const string Id = "classify-edit";

    public static Dictionary<string, string> Variables(
        string articleTitle, DateTimeOffset editTimestamp, string editorKind, string exposure,
        string editSummary, string revertSummary, string removedText, string insertedText, string context) => new()
    {
        ["article_title"] = articleTitle,
        ["edit_timestamp"] = editTimestamp.UtcDateTime.ToString("yyyy-MM-dd HH:mm 'UTC'"),
        ["editor_kind"] = editorKind,
        ["exposure"] = exposure,
        ["edit_summary"] = editSummary,
        ["revert_summary"] = revertSummary,
        ["removed_text"] = removedText,
        ["inserted_text"] = insertedText,
        ["context"] = context,
    };
}
