namespace AmpersandOfTruth.Web.Features.Articles;

public static class ArticlesFeature
{
    public static IServiceCollection AddArticlesFeature(this IServiceCollection services)
    {
        services.AddSingleton<IArticleCorpus, MariaDbArticleCorpus>();
        services.AddSingleton<IConflictEpisodes, MariaDbConflictEpisodes>();
        services.AddSingleton<ArticleTimelineService>();
        return services;
    }
}
