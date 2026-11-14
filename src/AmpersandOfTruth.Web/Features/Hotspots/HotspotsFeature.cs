namespace AmpersandOfTruth.Web.Features.Hotspots;

public static class HotspotsFeature
{
    public static IServiceCollection AddHotspotsFeature(this IServiceCollection services)
    {
        services.AddSingleton<MariaDbHotspotStore>();
        services.AddSingleton<IHotspotStore>(static sp => sp.GetRequiredService<MariaDbHotspotStore>());
        return services;
    }
}
