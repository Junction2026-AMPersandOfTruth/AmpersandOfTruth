namespace AmpersandOfTruth.Web.Features.Demo;

public static class DemoFeature
{
    public static IServiceCollection AddDemoFeature(this IServiceCollection services)
    {
        services.AddOptions<DemoOptions>()
            .Configure<IConfiguration>((options, config) =>
            {
                var section = config.GetSection(DemoOptions.Section);
                if (section.GetSection(nameof(DemoOptions.SeedTopics)).Exists())
                {
                    options.SeedTopics.Clear();
                }
            })
            .BindConfiguration(DemoOptions.Section);

        services.AddHttpContextAccessor();
        services.AddSingleton<ColdRunGate>();
        services.AddSingleton<StartLimiter>();
        services.AddScoped<ClientIdentity>();
        services.AddHostedService<DemoLibrarySeeder>();
        return services;
    }
}
