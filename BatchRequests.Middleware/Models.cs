namespace BatchRequests.Middleware;

public class BatchRequest
{
    public required string Method { get; set; }
    public required string RelativeUrl { get; set; }
    public List<BatchHeader>? AdditionalHeaders { get; set; }
    public object? Body { get; set; }
}

public class BatchHeader
{
    public required string Key { get; set; }
    public required string Value { get; set; }
}

public class BatchResponse
{
    public int StatusCode { get; set; }
    public List<BatchHeader> Headers { get; set; } = [];
    public object? Body { get; set; }
}
