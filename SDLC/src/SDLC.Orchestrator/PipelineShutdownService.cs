using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SDLC.Infrastructure;

namespace SDLC.Orchestrator;

public class PipelineShutdownService(
    PipelineRunnerService runner,
    IRunStore runStore,
    ILogger<PipelineShutdownService> logger)
    : IHostedService
{
    private const int ShutdownTimeoutSeconds = 30;

    public Task StartAsync(CancellationToken ct) => Task.CompletedTask;

    public async Task StopAsync(CancellationToken ct)
    {
        try
        {
            var inFlight = runner.AllInFlightTasks();
            if (!inFlight.Any())
            {
                logger.LogInformation("No in-flight pipeline tasks to shut down.");
                return;
            }

            logger.LogInformation(
                "Shutting down: {Count} in-flight pipeline tasks. Timeout: {Timeout}s.",
                inFlight.Count(), ShutdownTimeoutSeconds);

            var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(ShutdownTimeoutSeconds));

            try
            {
                await Task.WhenAll(inFlight);
                logger.LogInformation("All in-flight pipeline tasks completed before shutdown timeout.");
            }
            catch (OperationCanceledException)
            {
                logger.LogWarning(
                    "Shutdown timeout ({Timeout}s) reached. {Count} tasks did not complete.",
                    ShutdownTimeoutSeconds, inFlight.Count());
            }

            // Persist failed state for each still-active run
            var activeRunIds = runner.GetAllActiveRunIds();
            foreach (var runId in activeRunIds)
            {
                try
                {
                    var checkpoint = await runStore.GetRunAsync(runId);
                    if (checkpoint != null)
                    {
                        logger.LogInformation(
                            "Persisting failed state for run {RunId} at stage {Stage}.",
                            runId, checkpoint.CurrentStage);
                        await runStore.UpdateStageAsync(runId, checkpoint.CurrentStage, "Failed");
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex,
                        "Failed to persist shutdown state for run {RunId}.", runId);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error during pipeline shutdown.");
        }
    }
}
