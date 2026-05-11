using SDLC.Contracts;

namespace SDLC.Dashboard.Services;

public class VllmHealthCheck(ModelRoutingConfig routing, HttpClient? http = null)
{
    private readonly HttpClient _http = http ?? new() { Timeout = TimeSpan.FromSeconds(5) };

    public async Task<(bool Healthy, string Message)> CheckAsync(CancellationToken ct = default)
    {
        var endpoints = Enum.GetValues<SdlcStage>()
            .Select(s => routing.GetEndpoint(s))
            .DistinctBy(e => e.BaseUrl);

        var failures = new List<string>();
        foreach (var endpoint in endpoints)
        {
            try
            {
                var response = await _http.GetAsync($"{endpoint.BaseUrl}/v1/models", ct);
                if (!response.IsSuccessStatusCode)
                    failures.Add($"{endpoint.BaseUrl}: {response.StatusCode}");
            }
            catch (Exception ex)
            {
                failures.Add($"{endpoint.BaseUrl}: {ex.Message}");
            }
        }

        return failures.Count == 0
            ? (true, "All vLLM endpoints reachable")
            : (false, $"Unreachable: {string.Join(", ", failures)}");
    }
}
