namespace AmpersandOfTruth.Web.Features.Sharing;

public static class SharingFeature
{
    public static IServiceCollection AddSharingFeature(this IServiceCollection services)
    {
        services.AddSingleton<ShareLinkStore>();
        return services;
    }
}
