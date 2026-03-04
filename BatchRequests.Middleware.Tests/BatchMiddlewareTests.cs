using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace BatchRequests.Middleware.Tests;

public class BatchMiddlewareTests : IAsyncLifetime
{
    private IHost _host = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _host = await BatchTestHost.CreateHost();
        _client = _host.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _host.StopAsync();
        _host.Dispose();
    }

    [Fact]
    public async Task NonBatchRequest_PassesThrough()
    {
        var response = await _client.GetAsync("/api/hello");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("hello world", json.GetProperty("message").GetString());
    }

    [Fact]
    public async Task SingleGetRequest_ReturnsBatchResponse()
    {
        var batch = new[]
        {
            new { method = "GET", relativeUrl = "/api/hello" }
        };

        var response = await _client.PostAsJsonAsync("/api/batch", batch);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var results = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, results.GetArrayLength());

        var first = results[0];
        Assert.Equal(200, first.GetProperty("statusCode").GetInt32());
        Assert.Equal("hello world", first.GetProperty("body").GetProperty("message").GetString());
    }

    [Fact]
    public async Task PostWithBody_ForwardsBody()
    {
        var batch = new[]
        {
            new
            {
                method = "POST",
                relativeUrl = "/api/echo",
                body = (object)new { greeting = "hi" }
            }
        };

        var response = await _client.PostAsJsonAsync("/api/batch", batch);

        var results = await response.Content.ReadFromJsonAsync<JsonElement>();
        var body = results[0].GetProperty("body");
        Assert.Equal("hi", body.GetProperty("greeting").GetString());
    }

    [Fact]
    public async Task AdditionalHeaders_AreMerged()
    {
        var batch = new[]
        {
            new
            {
                method = "GET",
                relativeUrl = "/api/headers",
                additionalHeaders = new[]
                {
                    new { key = "X-Custom", value = "test-value" }
                }
            }
        };

        var response = await _client.PostAsJsonAsync("/api/batch", batch);

        var results = await response.Content.ReadFromJsonAsync<JsonElement>();
        var body = results[0].GetProperty("body");
        Assert.Equal("test-value", body.GetProperty("X-Custom").GetString());
    }

    [Fact]
    public async Task MultipleRequests_AllProcessed()
    {
        var batch = new[]
        {
            new { method = "GET", relativeUrl = "/api/hello" },
            new { method = "GET", relativeUrl = "/api/text" },
            new { method = "GET", relativeUrl = "/api/not-found" }
        };

        var response = await _client.PostAsJsonAsync("/api/batch", batch);

        var results = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(3, results.GetArrayLength());
        Assert.Equal(200, results[0].GetProperty("statusCode").GetInt32());
        Assert.Equal(200, results[1].GetProperty("statusCode").GetInt32());
        Assert.Equal(404, results[2].GetProperty("statusCode").GetInt32());
    }

    [Fact]
    public async Task NonJsonResponse_ReturnedAsString()
    {
        var batch = new[]
        {
            new { method = "GET", relativeUrl = "/api/text" }
        };

        var response = await _client.PostAsJsonAsync("/api/batch", batch);

        var results = await response.Content.ReadFromJsonAsync<JsonElement>();
        var body = results[0].GetProperty("body");
        Assert.Equal(JsonValueKind.String, body.ValueKind);
        Assert.Equal("plain text response", body.GetString());
    }

    [Fact]
    public async Task JsonResponse_ReturnedAsObject()
    {
        var batch = new[]
        {
            new { method = "GET", relativeUrl = "/api/hello" }
        };

        var response = await _client.PostAsJsonAsync("/api/batch", batch);

        var results = await response.Content.ReadFromJsonAsync<JsonElement>();
        var body = results[0].GetProperty("body");
        Assert.Equal(JsonValueKind.Object, body.ValueKind);
        Assert.Equal("hello world", body.GetProperty("message").GetString());
    }

    [Fact]
    public async Task CustomBatchPath_Works()
    {
        using var host = await BatchTestHost.CreateHost(o => o.BatchPath = "/custom/batch");
        using var client = host.GetTestClient();

        var batch = new[]
        {
            new { method = "GET", relativeUrl = "/api/hello" }
        };

        // Default path should pass through (404 since no endpoint matches)
        var defaultResponse = await client.PostAsJsonAsync("/api/batch", batch);
        Assert.Equal(HttpStatusCode.NotFound, defaultResponse.StatusCode);

        // Custom path should work
        var customResponse = await client.PostAsJsonAsync("/custom/batch", batch);
        Assert.Equal(HttpStatusCode.OK, customResponse.StatusCode);
        var results = await customResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, results.GetArrayLength());
        Assert.Equal("hello world", results[0].GetProperty("body").GetProperty("message").GetString());
    }

    [Fact]
    public async Task InnerRequestException_Returns500WithoutKillingBatch()
    {
        var batch = new[]
        {
            new { method = "GET", relativeUrl = "/api/throw" },
            new { method = "GET", relativeUrl = "/api/hello" }
        };

        var response = await _client.PostAsJsonAsync("/api/batch", batch);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var results = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, results.GetArrayLength());

        // First request threw — should be 500
        Assert.Equal(500, results[0].GetProperty("statusCode").GetInt32());

        // Second request should still succeed
        Assert.Equal(200, results[1].GetProperty("statusCode").GetInt32());
        Assert.Equal("hello world", results[1].GetProperty("body").GetProperty("message").GetString());
    }
}
