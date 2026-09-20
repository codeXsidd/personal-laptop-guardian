namespace LaptopGuardian.Agent.Backend;

public class BackendException : Exception
{
    public int StatusCode { get; }

    public BackendException(string message, int statusCode = 0, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }
}

public class BackendAuthenticationException : BackendException
{
    public BackendAuthenticationException(string message = "Authentication failed")
        : base(message, 401) { }
}

public class BackendRateLimitException : BackendException
{
    public TimeSpan? RetryAfter { get; }

    public BackendRateLimitException(TimeSpan? retryAfter = null)
        : base("Rate limit exceeded", 429)
    {
        RetryAfter = retryAfter;
    }
}
