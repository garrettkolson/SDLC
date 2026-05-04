using SDLC.Contracts;
using SDLC.Infrastructure;

namespace SDLC.Orchestrator;

public interface ISdlcProcessFactory
{
    ProcessHandle StartAsync(SdlcRunConfig config);
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

public class ProcessHandle
{
    private readonly Task _runTask;

    public ProcessHandle(Task runTask)
    {
        _runTask = runTask;
    }

    public Task Task => _runTask;
}
