using SDLC.Contracts;

namespace SDLC.Dashboard.Services;

public class VllmHealthCheck(ModelRoutingConfig routing)
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(5) };

    public async Task<(bool Healthy, string Message)> CheckAsync(CancellationToken ct = default)
    {
        var endpoint = routing.GetEndpoint(SdlcStage.Research);
        var url = $"{endpoint.BaseUrl}/v1/models";

        try
        {
            var response = await _http.GetAsync(url, ct);
            if (response.IsSuccessStatusCode)
                return (true, $"vLLM endpoint reachable at {endpoint.BaseUrl}");

            return (false, $"vLLM returned {response.StatusCode} at {endpoint.BaseUrl}");
        }
        catch (Exception ex)
        {
            return (false, $"vLLM unreachable at {endpoint.BaseUrl}: {ex.Message}");
        }
    }
}
