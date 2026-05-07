using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace SDLC.Orchestrator;

public class PipelineRecoveryHostedService(
    PipelineRunnerService runner,
    ILogger<PipelineRecoveryHostedService> logger)
    : IHostedService
{
    public async Task StartAsync(CancellationToken ct)
    {
        try
        {
            await runner.RecoverPendingGatesAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to recover pipeline state on startup");
        }
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
