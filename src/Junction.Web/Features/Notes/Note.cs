namespace Junction.Web.Features.Notes;

public sealed record Note(Guid Id, string Text, DateTimeOffset CreatedAt);
