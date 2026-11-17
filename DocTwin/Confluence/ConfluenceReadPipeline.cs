using System.Net;
using System.Net.Http;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using Serilog;

namespace DocTwin.Confluence;

internal static class ConfluenceReadPipeline
{
    public static bool IsTransient(HttpStatusCode status) => status is HttpStatusCode.TooManyRequests
        or HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout;

    public static TimeSpan RetryDelay(HttpResponseMessage response, int attempt)
    {
        var retryAfter = response.Headers.RetryAfter;
        var delay = retryAfter?.Delta ?? (retryAfter?.Date is DateTimeOffset date
            ? date - DateTimeOffset.UtcNow : TimeSpan.FromSeconds(1 << attempt));
        return delay < TimeSpan.Zero ? TimeSpan.Zero : delay;
    }

    public static ResiliencePipeline<HttpResponseMessage> Create() => new ResiliencePipelineBuilder<HttpResponseMessage>()
        .AddConcurrencyLimiter(2, 0)
        .AddCircuitBreaker(new CircuitBreakerStrategyOptions<HttpResponseMessage>
        {
            ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                .Handle<HttpRequestException>().HandleResult(response => IsTransient(response.StatusCode)),
            FailureRatio = 0.75,
            MinimumThroughput = 4,
            SamplingDuration = TimeSpan.FromSeconds(30),
            BreakDuration = TimeSpan.FromSeconds(30),
            OnOpened = _ => { Log.Warning("ConfluenceCircuitOpened"); return default; },
            OnClosed = _ => { Log.Information("ConfluenceCircuitClosed"); return default; }
        })
        .AddRetry(new RetryStrategyOptions<HttpResponseMessage>
        {
            MaxRetryAttempts = 2,
            ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                .Handle<HttpRequestException>()
                .HandleResult(response => IsTransient(response.StatusCode) && RetryDelay(response, 0) <= TimeSpan.FromSeconds(30)),
            DelayGenerator = args => new ValueTask<TimeSpan?>(
                (args.Outcome.Result is { } response ? RetryDelay(response, args.AttemptNumber)
                    : TimeSpan.FromSeconds(1 << args.AttemptNumber)) + TimeSpan.FromMilliseconds(Random.Shared.Next(100, 400))),
            OnRetry = args =>
            {
                Log.Warning("ConfluenceReadRetry {Attempt} {StatusCode}", args.AttemptNumber + 1,
                    args.Outcome.Result is { } response ? (int)response.StatusCode : 0);
                args.Outcome.Result?.Dispose();
                return default;
            }
        })
        .Build();
}
