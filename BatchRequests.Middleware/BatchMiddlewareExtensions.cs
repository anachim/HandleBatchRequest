using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace BatchRequests.Middleware;

public static class BatchMiddlewareExtensions
{
    public static IApplicationBuilder UseBatchMiddleware(this IApplicationBuilder app)
    {
        return UseBatchMiddleware(app, new BatchMiddlewareOptions());
    }

    public static IApplicationBuilder UseBatchMiddleware(this IApplicationBuilder app, BatchMiddlewareOptions options)
    {
        var contextFactory = app.ApplicationServices.GetRequiredService<IHttpContextFactory>();
        return app.UseMiddleware<BatchMiddleware>(contextFactory, options);
    }

    public static IApplicationBuilder UseBatchMiddleware(this IApplicationBuilder app, Action<BatchMiddlewareOptions> configure)
    {
        var options = new BatchMiddlewareOptions();
        configure(options);
        return UseBatchMiddleware(app, options);
    }
}
