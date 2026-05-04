namespace SDLC.Contracts;

public class SdlcRunConfig
{
    public Guid RunId { get; init; } = Guid.NewGuid();
    public string ProjectBrief { get; init; } = "";
    public ModelRoutingConfig ModelRouting { get; init; } = new();
}
