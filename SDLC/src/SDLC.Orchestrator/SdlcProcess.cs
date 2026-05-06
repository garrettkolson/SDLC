using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using SDLC.Contracts;
using SDLC.Infrastructure;
using SDLC.Notifications;
using SDLC.Telemetry;

namespace SDLC.Orchestrator;


public class StageGateStep
{
    public async Task RequestApprovalAsync(
        IKernelProcessStepContext context,
        SdlcArtifact artifact,
        INotificationService notifications,
        IStageGateStore gateStore,
        ILogger<StageGateStep>? logger = null,
        CancellationToken ct = default)
    {
        var gate = await gateStore.CreateGateAsync(artifact);

        try
        {
            await notifications.SendApprovalRequestAsync(gate);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Notification failed for gate {GateId}. Gate remains pending — review manually.", gate.GateId);
        }

        await context.EmitEventAsync(new KernelProcessEvent
        {
            Id = SdlcEvents.GatePending,
            Data = gate.GateId
        }, ct);
    }
}

public class PipelineRunnerService(
    ISdlcProcessFactory processFactory,
    ILogger<PipelineRunnerService> logger,
    IPipelineTelemetry telemetry)
    : IPipelineRunner
{
    private readonly ConcurrentDictionary<Guid, object> _activeRuns = new();
    private readonly ConcurrentDictionary<Guid, TaskCompletionSource<GateResolution>> _pendingGates = new();

    public int ActiveRunCount => _activeRuns.Count;

    public virtual bool IsRunActive(Guid runId) => _activeRuns.ContainsKey(runId);

    public Task<GateResolution> WaitForGateAsync(Guid gateId, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<GateResolution>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingGates[gateId] = tcs;
        ct.Register(() => tcs.TrySetCanceled(ct));
        return tcs.Task;
    }

    public virtual async Task EnqueueAsync(SdlcRunConfig config, CancellationToken ct = default)
    {
        if (!_activeRuns.TryAdd(config.RunId, new object()))
            throw new InvalidOperationException($"Run {config.RunId} is already active.");

        logger.LogInformation("Starting SDLC run {RunId}", config.RunId);
        await telemetry.StartPipelineRunAsync(config.RunId, config.ProjectBrief, ct);

        var handle = processFactory.StartAsync(config);
        _ = handle.Task.ContinueWith(async t =>
        {
            _activeRuns.TryRemove(config.RunId, out _);
            await telemetry.CompletePipelineRunAsync(config.RunId, ct);
            if (t.IsFaulted)
                logger.LogError(t.Exception, "Run {RunId} failed", config.RunId);
            else
                logger.LogInformation("Run {RunId} completed", config.RunId);
        }, TaskScheduler.Default);
    }

    public virtual async Task ResumeGateAsync(Guid runId, Guid gateId, GateDecision decision, string? notes, CancellationToken ct = default)
    {
        if (!_activeRuns.ContainsKey(runId))
            throw new InvalidOperationException($"No active run for {runId}");

        if (_pendingGates.TryRemove(gateId, out var tcs))
        {
            tcs.TrySetResult(new GateResolution(gateId, decision, notes));
            if (decision == GateDecision.Approved)
                await telemetry.RecordGateApprovedAsync(gateId, ct);
            else if (decision == GateDecision.Rejected)
                await telemetry.RecordGateRejectedAsync(gateId, ct);
        }
    }
}
