using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using SDLC.Contracts;

namespace SDLC.Telemetry;

public record StepEvent(
    SdlcStage Stage,
    string StepName,
    bool Succeeded,
    DateTimeOffset Timestamp,
    string? Error = null);

public record GateEvent(
    Guid GateId,
    bool Approved,
    string? UserId,
    DateTimeOffset Timestamp);

public record PipelineEvent(
    Guid RunId,
    string? ProjectBrief,
    DateTimeOffset Started,
    DateTimeOffset? Ended);

public interface IPipelineTelemetry
{
    /// <summary>Starts a parent activity for a pipeline run.</summary>
    Activity? StartRunActivity(Guid runId);

    /// <summary>Starts a child activity for a pipeline stage.</summary>
    Activity? StartStageActivity(Guid runId, SdlcStage stage);

    Task RecordStepCompletedAsync(SdlcStage stage, string stepName, CancellationToken ct = default);
    Task RecordStepFailedAsync(SdlcStage stage, string stepName, Exception ex, CancellationToken ct = default);
    Task RecordGateApprovedAsync(Guid gateId, string? userId = null, CancellationToken ct = default);
    Task RecordGateRejectedAsync(Guid gateId, string? userId = null, CancellationToken ct = default);
    Task StartPipelineRunAsync(Guid runId, string projectBrief, CancellationToken ct = default);
    Task CompletePipelineRunAsync(Guid runId, CancellationToken ct = default);

    Task<IReadOnlyList<StepEvent>> GetStepEventsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<GateEvent>> GetGateEventsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<PipelineEvent>> GetPipelineEventsAsync(CancellationToken ct = default);
}

public class PipelineTelemetry : IPipelineTelemetry
{
    private readonly ConcurrentQueue<StepEvent> _stepEvents = new();
    private readonly ConcurrentQueue<GateEvent> _gateEvents = new();
    private readonly ConcurrentQueue<PipelineEvent> _pipelineEvents = new();

    public Activity? StartRunActivity(Guid runId)
    {
        var activity = SdlcTelemetry.ActivitySource.StartActivity(
            "SDLC.Pipeline.Run", ActivityKind.Server, null,
            new Dictionary<string, object?> { ["run.id"] = runId });
        return activity;
    }

    public Activity? StartStageActivity(Guid runId, SdlcStage stage)
    {
        if (Activity.Current is null)
        {
            return SdlcTelemetry.ActivitySource.StartActivity($"SDLC.Pipeline.{stage}");
        }

        return SdlcTelemetry.ActivitySource.StartActivity(
            $"SDLC.Pipeline.{stage}", ActivityKind.Internal, Activity.Current.Context,
            new Dictionary<string, object?> { ["run.id"] = runId });
    }

    public async Task RecordStepCompletedAsync(SdlcStage stage, string stepName, CancellationToken ct = default)
    {
        _stepEvents.Enqueue(new StepEvent(stage, stepName, true, DateTimeOffset.UtcNow));
    }

    public async Task RecordStepFailedAsync(SdlcStage stage, string stepName, Exception ex, CancellationToken ct = default)
    {
        _stepEvents.Enqueue(new StepEvent(stage, stepName, false, DateTimeOffset.UtcNow, ex.Message));
    }

    public async Task RecordGateApprovedAsync(Guid gateId, string? userId = null, CancellationToken ct = default)
    {
        SdlcTelemetry.GatesApproved.Add(1);
        _gateEvents.Enqueue(new GateEvent(gateId, true, userId, DateTimeOffset.UtcNow));
    }

    public async Task RecordGateRejectedAsync(Guid gateId, string? userId = null, CancellationToken ct = default)
    {
        SdlcTelemetry.GatesRejected.Add(1);
        _gateEvents.Enqueue(new GateEvent(gateId, false, userId, DateTimeOffset.UtcNow));
    }

    public async Task StartPipelineRunAsync(Guid runId, string projectBrief, CancellationToken ct = default)
    {
        SdlcTelemetry.RunsStarted.Add(1);
        _pipelineEvents.Enqueue(new PipelineEvent(runId, projectBrief, DateTimeOffset.UtcNow, null));
    }

    public async Task CompletePipelineRunAsync(Guid runId, CancellationToken ct = default)
    {
        SdlcTelemetry.RunsCompleted.Add(1);
        _pipelineEvents.Enqueue(new PipelineEvent(runId, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
    }

    public async Task<IReadOnlyList<StepEvent>> GetStepEventsAsync(CancellationToken ct = default)
    {
        return _stepEvents.ToList().AsReadOnly();
    }

    public async Task<IReadOnlyList<GateEvent>> GetGateEventsAsync(CancellationToken ct = default)
    {
        return _gateEvents.ToList().AsReadOnly();
    }

    public async Task<IReadOnlyList<PipelineEvent>> GetPipelineEventsAsync(CancellationToken ct = default)
    {
        return _pipelineEvents.ToList().AsReadOnly();
    }
}
