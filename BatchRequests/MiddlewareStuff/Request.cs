using System.Collections.Generic;

namespace BatchRequests.MiddlewareStuff
{
    public class Request
    {
        public string Method { get; set; }
        public string RelativeUrl { get; set; }
        public List<Header> AdditionalHeaders { get; set; }
        public object Body { get; set; }
    }

    public class Header
    {
        public string Key { get; set; }
        public string Value { get; set; }
    }

    public class Response
    {
        public int StatusCode { get; set; }
        public List<Header> Headers { get; set; }
        public object Body { get; set; }
    }
}