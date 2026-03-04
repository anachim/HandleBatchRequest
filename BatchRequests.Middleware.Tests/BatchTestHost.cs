using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace BatchRequests.Middleware.Tests;

public static class BatchTestHost
{
    public static async Task<IHost> CreateHost(Action<BatchMiddlewareOptions>? configure = null)
    {
        var host = await new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.ConfigureServices(services => services.AddRouting());
                webBuilder.Configure(app =>
                {
                    if (configure is not null)
                        app.UseBatchMiddleware(configure);
                    else
                        app.UseBatchMiddleware();

                    app.UseRouting();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapGet("/api/hello", async context =>
                        {
                            context.Response.ContentType = "application/json";
                            await JsonSerializer.SerializeAsync(context.Response.Body,
                                new { message = "hello world" });
                        });

                        endpoints.MapPost("/api/echo", async context =>
                        {
                            context.Response.ContentType = "application/json";
                            var body = await JsonSerializer.DeserializeAsync<JsonElement>(context.Request.Body);
                            await JsonSerializer.SerializeAsync(context.Response.Body, body);
                        });

                        endpoints.MapGet("/api/headers", async context =>
                        {
                            context.Response.ContentType = "application/json";
                            var xHeaders = context.Request.Headers
                                .Where(h => h.Key.StartsWith("X-", StringComparison.OrdinalIgnoreCase))
                                .ToDictionary(h => h.Key, h => h.Value.ToString());
                            await JsonSerializer.SerializeAsync(context.Response.Body, xHeaders);
                        });

                        endpoints.MapGet("/api/text", async context =>
                        {
                            context.Response.ContentType = "text/plain";
                            await context.Response.WriteAsync("plain text response");
                        });

                        endpoints.MapGet("/api/not-found", context =>
                        {
                            context.Response.StatusCode = StatusCodes.Status404NotFound;
                            return Task.CompletedTask;
                        });

                        endpoints.MapGet("/api/throw", _ =>
                        {
                            throw new InvalidOperationException("Boom");
                        });
                    });
                });
            })
            .StartAsync();

        return host;
    }
}
