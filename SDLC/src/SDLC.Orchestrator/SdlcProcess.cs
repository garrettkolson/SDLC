using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using SDLC.Contracts;
using SDLC.Infrastructure;
using SDLC.Notifications;
using SDLC.Orchestrator.Logging;
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
    IPipelineTelemetry telemetry,
    IStageGateStore gateStore,
    IRunStore runStore)
    : IPipelineRunner
{
    private readonly ConcurrentDictionary<Guid, Task> _activeRuns = new();
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _runCancellation = new();
    private readonly ConcurrentDictionary<Guid, TaskCompletionSource<GateResolution>> _pendingGates = new();

    public int ActiveRunCount => _activeRuns.Count;

    public virtual bool IsRunActive(Guid runId) => _activeRuns.ContainsKey(runId);

    public virtual Task<GateResolution> WaitForGateAsync(Guid gateId, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<GateResolution>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingGates[gateId] = tcs;
        ct.Register(() => tcs.TrySetCanceled(ct));
        return tcs.Task;
    }

    public virtual async Task EnqueueAsync(SdlcRunConfig config, CancellationToken ct = default)
    {
        using var _logScope = LogScope.ForRun(config.RunId);

        using var runActivity = telemetry.StartRunActivity(config.RunId);

        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _runCancellation[config.RunId] = cts;

        var handle = processFactory.StartAsync(config, cts.Token);

        if (!_activeRuns.TryAdd(config.RunId, handle.Task))
        {
            _runCancellation.TryRemove(config.RunId, out _);
            cts.Dispose();
            throw new InvalidOperationException($"Run {config.RunId} is already active.");
        }

        logger.LogInformation("Starting SDLC run {RunId}", config.RunId);
        await telemetry.StartPipelineRunAsync(config.RunId, config.ProjectBrief, ct);

        _ = handle.Task.ContinueWith(async t =>
        {
            using var runScope = LogScope.ForRun(config.RunId);

            _activeRuns.TryRemove(config.RunId, out _);
            _runCancellation.TryRemove(config.RunId, out _);
            cts.Dispose();

            // Persist final state before telemetry
            try
            {
                if (t.IsFaulted)
                    await runStore.UpdateStageAsync(config.RunId, "Failed", "Failed");
                else
                    await runStore.UpdateStageAsync(config.RunId, "Learn", "Completed");
            }
            catch (Exception persistEx)
            {
                logger.LogError(persistEx, "Failed to persist completion state for run {RunId}", config.RunId);
            }

            await telemetry.CompletePipelineRunAsync(config.RunId, ct);
            if (t.IsFaulted)
                logger.LogError(t.Exception, "Run {RunId} failed", config.RunId);
            else
                logger.LogInformation("Run {RunId} completed", config.RunId);
        }, TaskScheduler.Default);
    }

    public virtual async Task ResumeGateAsync(Guid runId, Guid gateId, GateDecision decision, string? notes, CancellationToken ct = default)
    {
        using var runScope = LogScope.ForRun(runId);
        using var gateScope = LogScope.ForGate(gateId);

        if (!_activeRuns.ContainsKey(runId))
            throw new InvalidOperationException($"No active run for {runId}");

        if (_pendingGates.TryRemove(gateId, out var tcs))
        {
            tcs.TrySetResult(new GateResolution(gateId, decision, notes));
            if (decision == GateDecision.Approved)
                await telemetry.RecordGateApprovedAsync(gateId, ct: ct);
            else if (decision == GateDecision.Rejected)
                await telemetry.RecordGateRejectedAsync(gateId, ct: ct);
        }
    }

    public async Task RecoverPendingGatesAsync()
    {
        var pendingGates = await gateStore.GetAllPendingAsync();
        foreach (var gate in pendingGates)
        {
            var tcs = new TaskCompletionSource<GateResolution>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pendingGates[gate.GateId] = tcs;
            _activeRuns.TryAdd(gate.RunId, Task.CompletedTask);
        }

        var incompleteRuns = await runStore.GetAllIncompleteAsync();
        foreach (var run in incompleteRuns)
        {
            _activeRuns.TryAdd(run.RunId, Task.CompletedTask);
        }

        foreach (var run in incompleteRuns)
        {
            var pendingForRun = await gateStore.GetPendingForRunAsync(run.RunId);
            if (pendingForRun.Count == 0)
            {
                logger.LogInformation("Auto-resuming run {RunId} at stage {Stage}", run.RunId, run.CurrentStage);
                await ResumeRunAsync(run);
            }
            else
            {
                logger.LogInformation("Run {RunId} blocked on pending gate at stage {Stage}", run.RunId, run.CurrentStage);
            }
        }

        if (pendingGates.Count > 0)
            logger.LogInformation("Recovered {Count} pending gates on startup", pendingGates.Count);
    }

    private async Task ResumeRunAsync(RunCheckpoint run)
    {
        var config = new SdlcRunConfig { RunId = run.RunId, ProjectBrief = "" };
        var cts = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken.None);
        _runCancellation[run.RunId] = cts;

        var handle = processFactory.ResumeAsync(config, run.CurrentStage, cts.Token);
        _ = handle.Task.ContinueWith(async t =>
        {
            using var runScope = LogScope.ForRun(run.RunId);

            _activeRuns.TryRemove(run.RunId, out _);
            _runCancellation.TryRemove(run.RunId, out _);
            cts.Dispose();

            // Persist final state before telemetry
            try
            {
                if (t.IsFaulted)
                    await runStore.UpdateStageAsync(run.RunId, "Failed", "Failed");
                else
                    await runStore.UpdateStageAsync(run.RunId, run.CurrentStage, "Completed");
            }
            catch (Exception persistEx)
            {
                logger.LogError(persistEx, "Failed to persist completion state for recovery run {RunId}", run.RunId);
            }

            await telemetry.CompletePipelineRunAsync(run.RunId, default);
            if (t.IsFaulted)
                logger.LogError(t.Exception, "Recovery run {RunId} failed", run.RunId);
            else
                logger.LogInformation("Recovery run {RunId} completed", run.RunId);
        }, TaskScheduler.Default);
    }

    /// <summary>
    /// Returns all in-flight pipeline tasks for graceful shutdown.
    /// Excludes recovered/dummy tasks (Task.CompletedTask).
    /// </summary>
    public virtual IReadOnlyCollection<Task> AllInFlightTasks()
    {
        return _activeRuns.Values
            .Where(t => t != Task.CompletedTask && !t.IsCompleted)
            .ToList()
            .AsReadOnly();
    }

    public virtual IReadOnlyCollection<Guid> GetAllActiveRunIds()
        => _activeRuns.Keys.ToList().AsReadOnly();

    public async Task CancelRunAsync(Guid runId, CancellationToken ct = default)
    {
        if (!_activeRuns.ContainsKey(runId))
            throw new InvalidOperationException($"No active run for {runId}");

        if (_runCancellation.TryRemove(runId, out var cts))
        {
            cts.Cancel();
            cts.Dispose();
        }
    }
}
