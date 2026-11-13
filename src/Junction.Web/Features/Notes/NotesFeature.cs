namespace Junction.Web.Features.Notes;

public static class NotesFeature
{
    public static IServiceCollection AddNotesFeature(this IServiceCollection services)
    {
        services.AddSingleton<NoteStore>();
        return services;
    }
}
