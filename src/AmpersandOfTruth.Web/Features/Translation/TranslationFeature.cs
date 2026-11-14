namespace AmpersandOfTruth.Web.Features.Translation;

public static class TranslationFeature
{
    public static IServiceCollection AddTranslationFeature(this IServiceCollection services)
    {
        services.AddSingleton<GeminiEvidenceTranslator>();
        services.AddSingleton<IEvidenceTranslator>(sp => sp.GetRequiredService<GeminiEvidenceTranslator>());
        services.AddScoped<LanguagePreference>();
        return services;
    }
}
