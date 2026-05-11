using Microsoft.Extensions.Logging;
using SDLC.Contracts;
using System.Net;
using System.Text;
using System.Text.Json;

namespace SDLC.Agents;

public interface IKernel
{
    Task<string> CompleteAsync(string systemPrompt, string userMessage, CancellationToken ct = default);
    Task<(string Content, TokenUsage Usage)> CompleteAsyncWithUsage(string systemPrompt, string userMessage, CancellationToken ct = default);
}

public class KernelException : Exception
{
    public KernelException(string message) : base(message) { }
    public KernelException(string message, Exception inner) : base(message, inner) { }
}

public class AgentKernelFactory(
    ModelRoutingConfig routing,
    IResilientHttpClientFactory resilientFactory)
    : IKernelFactory
{
    public IKernel CreateForStage(SdlcStage stage)
    {
        var endpoint = routing.GetEndpoint(stage);
        return new DefaultKernel(endpoint, resilientFactory, stage);
    }
}

public class DefaultKernel : IKernel
{
    private readonly ModelEndpoint _endpoint;
    private readonly HttpClient _http;
    private readonly ILogger<DefaultKernel>? _logger;

    public DefaultKernel(ModelEndpoint endpoint, IResilientHttpClientFactory factory, SdlcStage stage)
    {
        _endpoint = endpoint;
        _http = factory.CreateForStage(stage);
        _logger = null;
    }

    public async Task<string> CompleteAsync(string systemPrompt, string userMessage, CancellationToken ct = default)
    {
        var request = new
        {
            model = _endpoint.ModelId,
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user",   content = userMessage }
            },
            temperature = 0.7,
            max_tokens = _endpoint.MaxTokens ?? 4096
        };

        var body = JsonSerializer.Serialize(request);
        var content = new StringContent(body, Encoding.UTF8, "application/json");

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/v1/chat/completions") { Content = content };
        if (_endpoint.ApiKey is not null)
            httpRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _endpoint.ApiKey);
        var response = await _http.SendAsync(httpRequest, HttpCompletionOption.ResponseContentRead, ct);

        if (!response.IsSuccessStatusCode)
        {
            _logger?.LogWarning("vLLM returned {Status} for model {Model}", response.StatusCode, _endpoint.ModelId);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
                throw new KernelException($"vLLM rate limited ({response.StatusCode})");
            throw new KernelException($"vLLM error: {response.StatusCode} {response.ReasonPhrase}");
        }

        string contentStr;
        try
        {
            using var doc = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);

            if (!doc.RootElement.TryGetProperty("choices", out var choices)
                || choices.GetArrayLength() == 0
                || !choices[0].TryGetProperty("message", out var msg)
                || !msg.TryGetProperty("content", out var textProp))
            {
                _logger?.LogError("vLLM response missing choices/message/content for model {Model}", _endpoint.ModelId);
                throw new KernelException("vLLM response missing choices/message/content");
            }

            contentStr = textProp.GetString() ?? "";
        }
        catch (JsonException ex)
        {
            _logger?.LogError(ex, "vLLM returned non-JSON response for model {Model}", _endpoint.ModelId);
            throw new KernelException("Malformed vLLM response", ex);
        }

        return contentStr;
    }

    public async Task<(string Content, TokenUsage Usage)> CompleteAsyncWithUsage(string systemPrompt, string userMessage, CancellationToken ct = default)
    {
        var request = new
        {
            model = _endpoint.ModelId,
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user",   content = userMessage }
            },
            temperature = 0.7,
            max_tokens = _endpoint.MaxTokens ?? 4096
        };

        var body = JsonSerializer.Serialize(request);
        var content = new StringContent(body, Encoding.UTF8, "application/json");

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/v1/chat/completions") { Content = content };
        if (_endpoint.ApiKey is not null)
            httpRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _endpoint.ApiKey);
        var response = await _http.SendAsync(httpRequest, HttpCompletionOption.ResponseContentRead, ct);

        if (!response.IsSuccessStatusCode)
        {
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
                throw new KernelException($"vLLM rate limited ({response.StatusCode})");
            throw new KernelException($"vLLM error: {response.StatusCode} {response.ReasonPhrase}");
        }

        TokenUsage usage = TokenUsage.Zero;
        string contentStr;
        try
        {
            using var doc = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);

            if (!doc.RootElement.TryGetProperty("choices", out var choices)
                || choices.GetArrayLength() == 0
                || !choices[0].TryGetProperty("message", out var msg)
                || !msg.TryGetProperty("content", out var textProp))
            {
                _logger?.LogError("vLLM response missing choices/message/content for model {Model}", _endpoint.ModelId);
                throw new KernelException("vLLM response missing choices/message/content");
            }

            contentStr = textProp.GetString() ?? "";

            if (doc.RootElement.TryGetProperty("usage", out var usageElem))
            {
                long pTokens = 0, cTokens = 0;
                if (usageElem.TryGetProperty("prompt_tokens", out var pt))
                    pTokens = pt.GetInt64();
                if (usageElem.TryGetProperty("completion_tokens", out var ctProp))
                    cTokens = ctProp.GetInt64();
                usage = new TokenUsage(pTokens, cTokens);
            }
        }
        catch (JsonException ex)
        {
            _logger?.LogError(ex, "vLLM returned non-JSON response for model {Model}", _endpoint.ModelId);
            throw new KernelException("Malformed vLLM response", ex);
        }

        return (contentStr, usage);
    }
}
