using System.Net;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Extensions.Http;

namespace SDLC.Notifications;

public class ResilientSlackHandler(
    ILogger<ResilientSlackHandler>? logger = null)
    : DelegatingHandler
{
    private readonly IAsyncPolicy<HttpResponseMessage> _retryPolicy = HttpPolicyExtensions
        .HandleTransientHttpError()
        .OrResult(r => r.StatusCode == HttpStatusCode.TooManyRequests)
        .WaitAndRetryAsync(
            3,
            i => TimeSpan.FromMilliseconds(Math.Pow(2, i) * 500 + Random.Shared.Next(0, 250)),
            (outcome, time, retryCount, context) =>
            {
                var status = outcome.Exception?.GetType().Name ?? outcome.Result?.StatusCode.ToString() ?? "unknown";
                logger?.LogWarning("Retry {Retry}/3 for Slack: {Status} after {Elapsed}ms",
                    retryCount, status, (int)time.TotalMilliseconds);
            });

    private readonly IAsyncPolicy<HttpResponseMessage> _timeoutPolicy = Policy.TimeoutAsync<HttpResponseMessage>(TimeSpan.FromSeconds(30));

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        return await _retryPolicy.ExecuteAsync(
            ct => _timeoutPolicy.ExecuteAsync(
                ct => base.SendAsync(request, ct),
                ct),
            cancellationToken);
    }
}
