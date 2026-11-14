using AmpersandOfTruth.Web.Features.Library;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace AmpersandOfTruth.Web.Features.Annotations;

public sealed class AnnotationsOptions
{
    public const string Section = "Annotations";

    /// <summary>
    /// Null = follow <see cref="IHostEnvironment.IsDevelopment"/> (defaults to true in tests when no host environment is registered).
    /// </summary>
    public bool? ReviewPageEnabled { get; set; }
}

public static class AnnotationsFeature
{
    public static IServiceCollection AddAnnotationsFeature(this IServiceCollection services)
    {
        services.AddOptions<AnnotationsOptions>().BindConfiguration(AnnotationsOptions.Section);
        services.TryAddSingleton<AnnotationStore>();
        services.TryAddSingleton<LibraryStore>();
        services.TryAddScoped<OwnerKey>();
        services.AddTransient<IStartupFilter, AnnotationsStartupFilter>();
        return services;
    }

    internal static bool IsDevEnabled(IServiceProvider services)
    {
        var opts = services.GetService<IOptions<AnnotationsOptions>>()?.Value;
        if (opts?.ReviewPageEnabled is { } explicitValue)
        {
            return explicitValue;
        }

        var env = services.GetService<IHostEnvironment>();
        return env is null || env.IsDevelopment();
    }

    private sealed class AnnotationsStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) =>
            app =>
            {
                app.Use(async (context, nextMiddleware) =>
                {
                    if (HttpMethods.IsGet(context.Request.Method)
                        && context.Request.Path.Equals("/dev/annotations.jsonl", StringComparison.OrdinalIgnoreCase)
                        && IsDevEnabled(context.RequestServices))
                    {
                        var store = context.RequestServices.GetRequiredService<AnnotationStore>();
                        var items = await store.AllAnnotatedIncidentsAsync(context.RequestAborted);
                        var jsonl = AnnotationExport.ToGoldenJsonl(items);
                        context.Response.StatusCode = StatusCodes.Status200OK;
                        context.Response.ContentType = "application/x-ndjson; charset=utf-8";
                        context.Response.Headers.ContentDisposition = """attachment; filename="annotations.jsonl" """;
                        context.Response.Headers.ContentDisposition = context.Response.Headers.ContentDisposition.ToString().Trim();
                        await context.Response.WriteAsync(jsonl, context.RequestAborted);
                        return;
                    }

                    await nextMiddleware(context);
                });

                next(app);
            };
    }
}
