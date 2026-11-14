namespace AmpersandOfTruth.Web.Features.TimeMachine;

public static class TimeMachineFeature
{
    public static IServiceCollection AddTimeMachineFeature(this IServiceCollection services)
    {
        services.AddSingleton<ITimeMachineCorpus, MariaDbTimeMachineCorpus>();
        services.AddSingleton<TimeMachineService>();
        return services;
    }
}
