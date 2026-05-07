using System.Diagnostics;
using Microsoft.Extensions.Logging;
using SDLC.Contracts;
using SDLC.Infrastructure;
using SDLC.Telemetry;

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
        IPipelineTelemetry telemetry,
        ILogger<BuildStep> logger,
        CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        using var activity = telemetry.StartStageActivity(spec.RunId, SdlcStage.Build);
        try
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

            if (result is null)
            {
                result = new BuildResult
                {
                    RunId = spec.RunId,
                    Stage = SdlcStage.Build,
                    SweAfRunId = sweAfRunId,
                    Success = false,
                    Logs = "Build timed out or was cancelled before a terminal status was received."
                };
            }

            await artifacts.SaveAsync(result);
            await context.EmitEventAsync(new KernelProcessEvent
            {
                Id = SdlcEvents.BuildComplete,
                Data = result
            }, ct);
            await telemetry.RecordStepCompletedAsync(SdlcStage.Build, nameof(BuildStep), ct);
            activity?.SetStatus(ActivityStatusCode.Ok);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.AddTag("error.type", ex.GetType().Name);
            activity?.AddTag("error.message", ex.Message);
            await telemetry.RecordStepFailedAsync(SdlcStage.Build, nameof(BuildStep), ex, ct);
            throw;
        }
        finally
        {
            sw.Stop();
            SdlcTelemetry.StageDuration.Record(sw.ElapsedMilliseconds, new KeyValuePair<string, object?>[] { new("sdlc.stage", "Build") });
        }
    }
}
