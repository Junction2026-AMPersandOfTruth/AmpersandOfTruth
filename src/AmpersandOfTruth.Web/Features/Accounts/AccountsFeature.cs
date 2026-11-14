namespace AmpersandOfTruth.Web.Features.Accounts;

public static class AccountsFeature
{
    public static IServiceCollection AddAccountsFeature(this IServiceCollection services)
    {
        services.AddSingleton<MariaDbAccountCorpus>();
        services.AddSingleton<IAccountCorpus>(sp =>
            new CachedAccountCorpus(sp.GetRequiredService<MariaDbAccountCorpus>(), sp.GetRequiredService<TimeProvider>()));
        services.AddSingleton<MariaDbEditorProfileSource>();
        services.AddSingleton<IEditorProfileSource>(sp =>
            new CachedEditorProfileSource(sp.GetRequiredService<MariaDbEditorProfileSource>(), sp.GetRequiredService<TimeProvider>()));
        return services;
    }
}
