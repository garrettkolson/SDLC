using Microsoft.Extensions.Logging;
using SDLC.Contracts;
using SDLC.Infrastructure;

namespace SDLC.Agents;

public interface ISweAfClient
{
    Task<string> SubmitAsync(SweAfTask task, CancellationToken ct = default);
    IAsyncEnumerable<SweAfStatus> PollAsync(string runId, CancellationToken ct = default);
}

public class BuildStep
{
    public async Task RunAsync(
        IKernelProcessStepContext context,
        ArchitectureRecord architecture,
        RequirementsSpec spec,
        ISweAfClient sweAf,
        IArtifactStore artifacts,
        ILogger<BuildStep> logger,
        CancellationToken ct = default)
    {
        var task = new SweAfTask
        {
            Spec = spec.Content,
            Architecture = architecture.Content
        };

        logger.LogInformation("Triggering SWE-AF run for {RunId}", spec.RunId);
        var sweAfRunId = await sweAf.SubmitAsync(task, ct);

        BuildResult? result = null;
        await foreach (var status in sweAf.PollAsync(sweAfRunId, ct))
        {
            logger.LogInformation("SWE-AF {RunId} status: {State}", sweAfRunId, status.State);

            if (status.IsTerminal)
            {
                result = new BuildResult
                {
                    RunId = spec.RunId,
                    Stage = SdlcStage.Build,
                    SweAfRunId = sweAfRunId,
                    Success = status.State == SweAfState.Succeeded,
                    Logs = status.Logs ?? ""
                };
                break;
            }
        }

        await artifacts.SaveAsync(result!);
        await context.EmitEventAsync(new KernelProcessEvent
        {
            Id = SdlcEvents.BuildComplete,
            Data = result
        }, ct);
    }
}
