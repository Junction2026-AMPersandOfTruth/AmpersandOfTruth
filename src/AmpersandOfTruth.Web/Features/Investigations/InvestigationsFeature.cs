namespace AmpersandOfTruth.Web.Features.Investigations;

public static class InvestigationsFeature
{
    public static IServiceCollection AddInvestigationsFeature(this IServiceCollection services)
    {
        services.AddSingleton<IInvestigationCorpus, MariaDbInvestigationCorpus>();
        services.AddSingleton<InvestigationService>();
        return services;
    }
}
