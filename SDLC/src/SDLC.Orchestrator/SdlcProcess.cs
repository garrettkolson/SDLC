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

public class PipelineRunnerService : IPipelineRunner
{
    private readonly ISdlcProcessFactory _processFactory;
    private readonly ILogger<PipelineRunnerService> _logger;
    private readonly IPipelineTelemetry? _telemetry;
    private readonly ConcurrentDictionary<Guid, object> _activeRuns = new();
    private readonly ConcurrentDictionary<Guid, TaskCompletionSource<GateResolution>> _pendingGates = new();

    public PipelineRunnerService(ISdlcProcessFactory processFactory, ILogger<PipelineRunnerService> logger, IPipelineTelemetry? telemetry = null)
    {
        _processFactory = processFactory;
        _logger = logger;
        _telemetry = telemetry;
    }

    public int ActiveRunCount => _activeRuns.Count;

    public virtual bool IsRunActive(Guid runId) => _activeRuns.ContainsKey(runId);

    public Task<GateResolution> WaitForGateAsync(Guid gateId, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<GateResolution>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingGates[gateId] = tcs;
        ct.Register(() => tcs.TrySetCanceled(ct));
        return tcs.Task;
    }

    public virtual Task EnqueueAsync(SdlcRunConfig config, CancellationToken ct = default)
    {
        if (!_activeRuns.TryAdd(config.RunId, new object()))
            throw new InvalidOperationException($"Run {config.RunId} is already active.");

        _logger.LogInformation("Starting SDLC run {RunId}", config.RunId);
        if (_telemetry != null)
            _telemetry.StartPipelineRunAsync(config.RunId, config.ProjectBrief, ct);

        var handle = _processFactory.StartAsync(config);
        _ = handle.Task.ContinueWith(t =>
        {
            _activeRuns.TryRemove(config.RunId, out _);
            if (_telemetry != null)
                _telemetry.CompletePipelineRunAsync(config.RunId, ct);
            SdlcTelemetry.RunsCompleted.Add(1);
            if (t.IsFaulted)
                _logger.LogError(t.Exception, "Run {RunId} failed", config.RunId);
            else
                _logger.LogInformation("Run {RunId} completed", config.RunId);
        }, TaskScheduler.Default);

        return Task.CompletedTask;
    }

    public virtual async Task ResumeGateAsync(Guid runId, Guid gateId, GateDecision decision, string? notes, CancellationToken ct = default)
    {
        if (!_activeRuns.ContainsKey(runId))
            throw new InvalidOperationException($"No active run for {runId}");

        if (_pendingGates.TryRemove(gateId, out var tcs))
        {
            tcs.TrySetResult(new GateResolution(gateId, decision, notes));
            if (decision == GateDecision.Approved && _telemetry != null)
                await _telemetry.RecordGateApprovedAsync(gateId, ct);
            else if (decision == GateDecision.Rejected && _telemetry != null)
                await _telemetry.RecordGateRejectedAsync(gateId, ct);
            SdlcTelemetry.GatesApproved.Add(decision == GateDecision.Approved ? 1 : 0);
            SdlcTelemetry.GatesRejected.Add(decision == GateDecision.Rejected ? 1 : 0);
        }
    }
}
