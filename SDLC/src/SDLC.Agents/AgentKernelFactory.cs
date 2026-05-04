using SDLC.Contracts;

namespace SDLC.Agents;

public interface IKernel
{
    Task<string> CompleteAsync(string systemPrompt, string userMessage, CancellationToken ct = default);
}

public class AgentKernelFactory : IKernelFactory
{
    private readonly ModelRoutingConfig _routing;

    public AgentKernelFactory(ModelRoutingConfig routing)
    {
        _routing = routing;
    }

    public IKernel CreateForStage(SdlcStage stage) =>
        new DefaultKernel(_routing.GetEndpoint(stage));
}

public class DefaultKernel : IKernel
{
    private readonly ModelEndpoint _endpoint;

    public DefaultKernel(ModelEndpoint endpoint)
    {
        _endpoint = endpoint;
    }

    public Task<string> CompleteAsync(string systemPrompt, string userMessage, CancellationToken ct = default)
    {
        // TODO: Real HTTP call to vLLM endpoint
        return Task.FromResult($"Response for {_endpoint.ModelId}");
    }
}
