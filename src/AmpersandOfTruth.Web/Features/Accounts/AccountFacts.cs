namespace AmpersandOfTruth.Web.Features.Accounts;

public sealed record AccountArticle(long PageId, string Title, int Edits, int RemovedEdits);

public sealed record AccountFacts(
    string User,
    int Edits,
    int Articles,
    int RemovedEdits,
    DateTime? First,
    DateTime? Last,
    IReadOnlyList<(int Year, int Edits)> PerYear,
    IReadOnlyList<AccountArticle> TopArticles);
