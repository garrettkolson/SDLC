using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using SDLC.Contracts;
using SDLC.Infrastructure;
using SDLC.Notifications;

namespace SDLC.Orchestrator;


public class StageGateStep
{
    public async Task RequestApprovalAsync(
        IKernelProcessStepContext context,
        SdlcArtifact artifact,
        INotificationService notifications,
        IStageGateStore gateStore,
        CancellationToken ct = default)
    {
        var gate = await gateStore.CreateGateAsync(artifact);
        await notifications.SendApprovalRequestAsync(gate);
        await context.EmitEventAsync(new KernelProcessEvent
        {
            Id = SdlcEvents.GatePending,
            Data = gate.GateId
        }, ct);
    }
}

public class PipelineRunnerService
{
    private readonly ISdlcProcessFactory _processFactory;
    private readonly ILogger<PipelineRunnerService> _logger;
    private readonly ConcurrentDictionary<Guid, object> _activeRuns = new();

    public PipelineRunnerService(ISdlcProcessFactory processFactory, ILogger<PipelineRunnerService> logger)
    {
        _processFactory = processFactory;
        _logger = logger;
    }

    public int ActiveRunCount => _activeRuns.Count;

    public bool IsRunActive(Guid runId) => _activeRuns.ContainsKey(runId);

    public async Task EnqueueAsync(SdlcRunConfig config, CancellationToken ct = default)
    {
        if (!_activeRuns.TryAdd(config.RunId, new object()))
            throw new InvalidOperationException($"Run {config.RunId} is already active.");

        _logger.LogInformation("Starting SDLC run {RunId}", config.RunId);
    }

    public async Task ResumeGateAsync(Guid runId, Guid gateId, GateDecision decision, string? notes, CancellationToken ct = default)
    {
        if (!_activeRuns.ContainsKey(runId))
            throw new InvalidOperationException($"No active run for {runId}");
    }
}
