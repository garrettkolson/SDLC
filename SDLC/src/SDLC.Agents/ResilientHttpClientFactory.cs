using Microsoft.Extensions.Logging;
using Polly;
using Polly.Extensions.Http;
using SDLC.Contracts;
using System.Net;

namespace SDLC.Agents;

public interface IResilientHttpClientFactory
{
    HttpClient CreateForStage(SdlcStage stage);
}

/// <summary>
/// Creates per-stage HttpClient instances with Polly resilience.
/// Each stage targets its configured endpoint from ModelRoutingConfig.
/// Supports arbitrary inference servers (vLLM, OpenRouter, cloud providers).
/// </summary>
public class ResilientHttpClientFactory(
    ModelRoutingConfig routing,
    ILogger<ResilientHttpClientFactory>? logger = null)
    : IResilientHttpClientFactory
{
    // Per-stage retry/backoff config
    private static readonly Dictionary<SdlcStage, StageResilience> _policies = new()
    {
        [SdlcStage.Research]      = new(3, 1000),
        [SdlcStage.Requirements]  = new(3, 1000),
        [SdlcStage.Design]        = new(2, 1500),
        [SdlcStage.Build]         = new(4, 2000),
        [SdlcStage.Learn]         = new(3, 1000),
    };

    public HttpClient CreateForStage(SdlcStage stage)
    {
        var endpoint = routing.GetEndpoint(stage);
        var timeout = endpoint.Timeout ?? TimeSpan.FromMinutes(3);
        var resilience = _policies[stage];

        var innerHandler = new SocketsHttpHandler
        {
            AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate,
        };

        var retryPolicy = HttpPolicyExtensions
            .HandleTransientHttpError()
            .OrResult(r => r.StatusCode == HttpStatusCode.TooManyRequests)
            .WaitAndRetryAsync(
                resilience.Retries,
                i => TimeSpan.FromMilliseconds(Math.Pow(2, i) * resilience.BackoffMs + Random.Shared.Next(0, 250)),
                (outcome, time, retryCount, context) =>
                {
                    var status = outcome.Exception?.GetType().Name ?? outcome.Result?.StatusCode.ToString() ?? "unknown";
                    logger?.LogWarning("Retry {Retry}/{Total} for stage {Stage}: {Status} after {Elapsed}ms",
                        retryCount, resilience.Retries, stage, status, (int)time.TotalMilliseconds);
                });

        var timeoutPolicy = Policy.TimeoutAsync<HttpResponseMessage>(timeout);

        var resilienceHandler = new ResilienceHandler(
            resiliencePolicy: retryPolicy,
            timeoutPolicy: timeoutPolicy,
            innerHandler: innerHandler);

        var http = new HttpClient(resilienceHandler, disposeHandler: true);
        http.BaseAddress = new Uri(endpoint.BaseUrl);
        if (!string.IsNullOrEmpty(endpoint.ApiKey))
            http.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", endpoint.ApiKey);
        http.Timeout = timeout;

        return http;
    }
}

internal record StageResilience(int Retries, int BackoffMs);

/// <summary>
/// DelegatingHandler that applies Polly retry + timeout policies around HTTP calls.
/// </summary>
internal class ResilienceHandler : DelegatingHandler
{
    private readonly IAsyncPolicy<HttpResponseMessage> _resiliencePolicy;
    private readonly IAsyncPolicy<HttpResponseMessage> _timeoutPolicy;

    public ResilienceHandler(
        IAsyncPolicy<HttpResponseMessage> resiliencePolicy,
        IAsyncPolicy<HttpResponseMessage> timeoutPolicy,
        HttpMessageHandler innerHandler)
        : base(innerHandler)
    {
        _resiliencePolicy = resiliencePolicy;
        _timeoutPolicy = timeoutPolicy;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // Compose: retry wraps timeout wraps actual request
        return await _resiliencePolicy.ExecuteAsync(
            ct => _timeoutPolicy.ExecuteAsync(
                ct => base.SendAsync(request, ct),
                ct),
            cancellationToken);
    }
}
