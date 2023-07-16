using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.Features.Authentication;
using Newtonsoft.Json;

namespace BatchRequests.MiddlewareStuff
{
    public class BatchMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly IHttpContextFactory _contextFactory;

        private const string Path = "/api/batch";

        public BatchMiddleware(RequestDelegate next, IHttpContextFactory contextFactory)
        {
            _next = next;
            _contextFactory = contextFactory;
        }

        public Task Invoke(HttpContext httpContext)
        {
            if (!httpContext.Request.Path.Equals(Path))
            {
                return _next.Invoke(httpContext);
            }

            return InvokeBatchAsync(httpContext);
        }

        private async Task InvokeBatchAsync(HttpContext batchContext)
        {
            var requestList = await GetRequestList(batchContext);
            var responseArrays = new List<Response>();

            foreach (var request in requestList)
            {
                var response = await InvokeNextMiddleware(batchContext, request);
                responseArrays.Add(response);
            }
            
            var responseJson = JsonConvert.SerializeObject(responseArrays);
            batchContext.Response.StatusCode = 200;
            await batchContext.Response.WriteAsync(responseJson);
        }

        private static async Task<List<Request>> GetRequestList(HttpContext batchContext)
        {
            using var streamReader = new StreamReader(batchContext.Request.Body);
            var batchRequestBody = (await streamReader.ReadToEndAsync()).Trim();
            return JsonConvert.DeserializeObject<List<Request>>(batchRequestBody);
        }

        private async Task<Response> InvokeNextMiddleware(HttpContext batchContext, Request request)
        {
            var headers = AddMissingHeaders(batchContext.Request.Headers, request);
            var innerContext = CreateInnerContext(request, headers, batchContext);

            await _next.Invoke(innerContext);
            
            innerContext.Response.Body.Seek(0, SeekOrigin.Begin);

            var reader = new StreamReader(innerContext.Response.Body);

            var responseBody = await reader.ReadToEndAsync(); 

            return new Response
            {
                StatusCode = innerContext.Response.StatusCode,
                Headers = innerContext.Response.Headers.Select(x => new Header {Key = x.Key, Value = x.Value}).ToList(),
                Body = innerContext.Response.Headers.Any(x => x.Value.ToString().ToLower().Contains("application/json"))
                    ? JsonConvert.DeserializeObject(responseBody)
                    : responseBody
            };
        }

        private static HeaderDictionary AddMissingHeaders(IHeaderDictionary batchHeaders, Request request)
        {
            var headers = new HeaderDictionary();

            foreach (var header in batchHeaders)
            {
                headers.Add(header);
            }

            foreach (var x in request.AdditionalHeaders)
            {
                headers.Add(x.Key, x.Value);
            }

            return headers;
        }

        private HttpContext CreateInnerContext(Request request, HeaderDictionary headers, HttpContext batchContext)
        {
            var requestFeature = new HttpRequestFeature
            {
                Headers = headers,
                Method = request.Method,
                Path = request.RelativeUrl,
                PathBase = string.Empty,
                Protocol = batchContext.Request.Protocol,
                Scheme = batchContext.Request.Scheme,
                QueryString = string.Empty
            };

            var features = CreateDefaultFeatures(batchContext.Features);
            features.Set<IHttpRequestFeature>(requestFeature);
            var memoryStream = new MemoryStream();
            
            features.Set<IHttpResponseFeature>(new HttpResponseFeature());
            features.Set<IHttpResponseBodyFeature>(new StreamResponseBodyFeature(memoryStream));

            return _contextFactory.Create(features);
        }

        private FeatureCollection CreateDefaultFeatures(IFeatureCollection input)
        {
            var output = new FeatureCollection();
            output.Set(input.Get<IServiceProvidersFeature>());
            output.Set(input.Get<IHttpRequestIdentifierFeature>());
            output.Set(input.Get<IAuthenticationFeature>());
            output.Set(input.Get<IHttpAuthenticationFeature>());
            output.Set<IItemsFeature>(new ItemsFeature());
            return output;
        }
    }
}