using Microsoft.Extensions.Http;
using SDLC.Contracts;
using System.Text.Json;

namespace SDLC.Agents;

public interface IKernel
{
    Task<string> CompleteAsync(string systemPrompt, string userMessage, CancellationToken ct = default);
}

public class AgentKernelFactory(
    ModelRoutingConfig routing, 
    IHttpClientFactory? httpClientFactory)
    : IKernelFactory
{
    public AgentKernelFactory(ModelRoutingConfig routing)
        : this(routing, httpClientFactory: null) { }

    public IKernel CreateForStage(SdlcStage stage) =>
        httpClientFactory is not null
            ? new DefaultKernel(routing.GetEndpoint(stage), httpClientFactory)
            : new DefaultKernel(routing.GetEndpoint(stage));
}

public class DefaultKernel : IKernel
{
    private readonly ModelEndpoint _endpoint;
    private readonly HttpClient _http;

    public DefaultKernel(ModelEndpoint endpoint)
    {
        _endpoint = endpoint;
        _http = new HttpClient { BaseAddress = new Uri(endpoint.BaseUrl) };
        if (!string.IsNullOrEmpty(endpoint.ApiKey))
            _http.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", endpoint.ApiKey);
    }

    public DefaultKernel(ModelEndpoint endpoint, IHttpClientFactory httpClientFactory)
    {
        _endpoint = endpoint;
        _http = httpClientFactory.CreateClient();
        _http.BaseAddress = new Uri(endpoint.BaseUrl);
        if (!string.IsNullOrEmpty(endpoint.ApiKey))
            _http.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", endpoint.ApiKey);
    }

    public async Task<string> CompleteAsync(string systemPrompt, string userMessage, CancellationToken ct = default)
    {
        // TODO: make these parameters configurable
        var request = new
        {
            model = _endpoint.ModelId,
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user",   content = userMessage  }
            },
            temperature = 0.7,
            max_tokens = 4096
        };

        var body = JsonSerializer.Serialize(request);
        var content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
        var response = await _http.PostAsync("/v1/chat/completions", content, ct);
        response.EnsureSuccessStatusCode();

        using var doc = await System.Text.Json.JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        return doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString() ?? "";
    }
}
