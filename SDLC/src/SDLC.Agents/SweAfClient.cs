using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using SDLC.Contracts;

namespace SDLC.Agents;

public class SweAfClient : ISweAfClient
{
    private readonly HttpClient _http;

    public SweAfClient(HttpClient http)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
    }

    public async Task<string> SubmitAsync(SweAfTask task, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync("/api/runs", task, ct);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<SweAfRunCreated>(ct)
            ?? throw new InvalidOperationException("SWE-AF returned empty run creation response.");
        return result.RunId;
    }

    public async IAsyncEnumerable<SweAfStatus> PollAsync(string runId, [EnumeratorCancellation] CancellationToken ct = default)
    {
        while (!ct.IsCancellationRequested)
        {
            var response = await _http.GetAsync($"/api/runs/{runId}", ct);
            response.EnsureSuccessStatusCode();
            var status = await response.Content.ReadFromJsonAsync<SweAfStatus>(ct)
                ?? throw new InvalidOperationException("SWE-AF returned empty poll response.");
            yield return status;

            if (status.IsTerminal)
                yield break;

            await Task.Delay(TimeSpan.FromSeconds(5), ct);
        }
    }
}
