namespace AmpersandOfTruth.Web.Features.Library;

public static class LibraryFeature
{
    public static IServiceCollection AddLibraryFeature(this IServiceCollection services)
    {
        services.AddSingleton<LibraryStore>();
        services.AddSingleton<SavedReplay>();
        services.AddScoped<OwnerKey>();
        return services;
    }
}
