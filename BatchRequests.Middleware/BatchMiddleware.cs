using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;

namespace BatchRequests.Middleware;

public class BatchMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IHttpContextFactory _contextFactory;
    private readonly BatchMiddlewareOptions _options;
    private readonly ILogger<BatchMiddleware> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public BatchMiddleware(RequestDelegate next, IHttpContextFactory contextFactory, BatchMiddlewareOptions options, ILogger<BatchMiddleware> logger)
    {
        _next = next;
        _contextFactory = contextFactory;
        _options = options;
        _logger = logger;
    }

    public Task Invoke(HttpContext httpContext)
    {
        if (!httpContext.Request.Path.Equals(_options.BatchPath, StringComparison.OrdinalIgnoreCase))
        {
            return _next.Invoke(httpContext);
        }

        return InvokeBatchAsync(httpContext);
    }

    private async Task InvokeBatchAsync(HttpContext batchContext)
    {
        var requestList = await JsonSerializer.DeserializeAsync<List<BatchRequest>>(
            batchContext.Request.Body, JsonOptions);
        var requests = requestList ?? [];

        _logger.LogInformation("Batch request received with {Count} sub-request(s)", requests.Count);

        var responses = new List<BatchResponse>();

        foreach (var request in requests)
        {
            var response = await ExecuteInnerRequest(batchContext, request);
            responses.Add(response);
        }

        batchContext.Response.StatusCode = StatusCodes.Status200OK;
        batchContext.Response.ContentType = "application/json";
        await JsonSerializer.SerializeAsync(batchContext.Response.Body, responses, JsonOptions);
    }

    private async Task<BatchResponse> ExecuteInnerRequest(HttpContext batchContext, BatchRequest request)
    {
        var headers = BuildHeaders(batchContext.Request.Headers, request.AdditionalHeaders);
        using var responseBody = new MemoryStream();
        var innerContext = CreateInnerContext(request, headers, batchContext, responseBody);

        try
        {
            await _next.Invoke(innerContext);

            responseBody.Seek(0, SeekOrigin.Begin);
            using var reader = new StreamReader(responseBody);
            var body = await reader.ReadToEndAsync();

            var contentType = innerContext.Response.ContentType;
            var isJson = contentType?.Contains("application/json", StringComparison.OrdinalIgnoreCase) == true;

            return new BatchResponse
            {
                StatusCode = innerContext.Response.StatusCode,
                Headers = innerContext.Response.Headers
                    .Select(x => new BatchHeader { Key = x.Key, Value = x.Value! })
                    .ToList(),
                Body = isJson ? JsonSerializer.Deserialize<JsonElement>(body) : body
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Inner batch request failed: {Method} {Url}", request.Method, request.RelativeUrl);

            return new BatchResponse
            {
                StatusCode = StatusCodes.Status500InternalServerError
            };
        }
        finally
        {
            _contextFactory.Dispose(innerContext);
        }
    }

    private static HeaderDictionary BuildHeaders(IHeaderDictionary batchHeaders, List<BatchHeader>? additionalHeaders)
    {
        var headers = new HeaderDictionary();

        foreach (var header in batchHeaders)
        {
            headers.Add(header);
        }

        if (additionalHeaders is null)
            return headers;

        foreach (var header in additionalHeaders)
        {
            headers[header.Key] = header.Value;
        }

        return headers;
    }

    private HttpContext CreateInnerContext(
        BatchRequest request,
        HeaderDictionary headers,
        HttpContext batchContext,
        MemoryStream responseBody)
    {
        var requestBodyStream = new MemoryStream();
        if (request.Body is not null)
        {
            JsonSerializer.Serialize(requestBodyStream, request.Body);
            requestBodyStream.Seek(0, SeekOrigin.Begin);

            if (!headers.ContainsKey(HeaderNames.ContentType))
            {
                headers[HeaderNames.ContentType] = "application/json";
            }
        }

        var requestFeature = new HttpRequestFeature
        {
            Headers = headers,
            Method = request.Method,
            Path = request.RelativeUrl,
            PathBase = string.Empty,
            Protocol = batchContext.Request.Protocol,
            Scheme = batchContext.Request.Scheme,
            QueryString = string.Empty,
            Body = requestBodyStream
        };

        var features = CreateDefaultFeatures(batchContext.Features);
        features.Set<IHttpRequestFeature>(requestFeature);
        features.Set<IHttpResponseFeature>(new HttpResponseFeature());
        features.Set<IHttpResponseBodyFeature>(new StreamResponseBodyFeature(responseBody));

        return _contextFactory.Create(features);
    }

    private static FeatureCollection CreateDefaultFeatures(IFeatureCollection input)
    {
        var output = new FeatureCollection();
        output.Set(input.Get<IServiceProvidersFeature>());
        output.Set(input.Get<IHttpRequestIdentifierFeature>());
        output.Set<IItemsFeature>(new ItemsFeature());
        return output;
    }
}
