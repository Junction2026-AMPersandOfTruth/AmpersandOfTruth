namespace AmpersandOfTruth.Web.Features.Accounts;

public static class AccountsFeature
{
    public static IServiceCollection AddAccountsFeature(this IServiceCollection services)
    {
        services.AddSingleton<MariaDbAccountCorpus>();
        services.AddSingleton<IAccountCorpus>(sp =>
            new CachedAccountCorpus(sp.GetRequiredService<MariaDbAccountCorpus>(), sp.GetRequiredService<TimeProvider>()));
        return services;
    }
}
