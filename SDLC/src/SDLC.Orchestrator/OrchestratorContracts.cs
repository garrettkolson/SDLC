using SDLC.Contracts;
using SDLC.Infrastructure;

namespace SDLC.Orchestrator;

public interface ISdlcProcessFactory
{
    ProcessHandle StartAsync(SdlcRunConfig config, CancellationToken ct = default);
    ProcessHandle ResumeAsync(SdlcRunConfig config, string stage, CancellationToken ct = default);
}

// Minimal SK Process abstraction for testability
public interface IProcessRuntime
{
    Task StartAsync(KernelProcessEvent initialEvent, CancellationToken ct);
    Task SendEventAsync(KernelProcessEvent eventMessage);
    Task WaitForCompletionAsync(CancellationToken ct);
}

public interface IKernelProcess
{
    IProcessRuntime CreateRuntime();
}

public class ProcessHandle(Task runTask)
{
    public Task Task => runTask;
}

public record GateResolution(Guid GateId, GateDecision Decision, string? Notes);

public interface IPipelineRunner
{
    bool IsRunActive(Guid runId);
    Task EnqueueAsync(SdlcRunConfig config, CancellationToken ct = default);
    Task ResumeGateAsync(Guid runId, Guid gateId, GateDecision decision, string? notes, CancellationToken ct = default);
    Task CancelRunAsync(Guid runId, CancellationToken ct = default);
}
