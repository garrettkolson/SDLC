namespace SDLC.Contracts;

public record ModelEndpoint(
    string ModelId,
    string BaseUrl,
    string? ApiKey = null,
    int? MaxTokens = null,
    TimeSpan? Timeout = null)
{
    public static readonly ModelEndpoint Local27B  = new(
        "codgician/Qwen3.5-27B-...", "http://localhost:8000/v1",
        MaxTokens: 4096, Timeout: TimeSpan.FromMinutes(3));
    public static readonly ModelEndpoint LocalMoE  = new(
        "Qwen3.5-35B-A3B", "http://localhost:8001/v1",
        MaxTokens: 8192, Timeout: TimeSpan.FromMinutes(5));
}
