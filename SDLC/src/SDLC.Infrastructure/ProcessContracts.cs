namespace SDLC.Infrastructure;

public class KernelProcessEvent
{
    public string Id { get; init; } = "";
    public object? Data { get; init; }
}

public interface IKernelProcessStepContext
{
    Task EmitEventAsync(KernelProcessEvent @event, CancellationToken ct = default);
}
