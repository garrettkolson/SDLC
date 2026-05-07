using SDLC.Contracts;
using SDLC.Infrastructure;
using SDLC.Orchestrator;
using SDLC.Telemetry;

namespace SDLC.Dashboard.Services;

public record RunSummary(
    Guid RunId,
    bool IsActive,
    SdlcStage LastStage,
    IReadOnlyList<GateSummary> PendingGates);

public record GateSummary(
    Guid GateId,
    SdlcStage Stage,
    GateStatus Status,
    string? Notes);

public interface ISdlcRunService
{
    Task<IReadOnlyList<RunSummary>> GetActiveRunsAsync(CancellationToken ct = default);
    Task<RunDetail?> GetRunDetailAsync(Guid runId, CancellationToken ct = default);
    Task ApproveGateAsync(Guid gateId, string approverUserId, string approverDisplayName, string? notes, CancellationToken ct = default);
    Task RejectGateAsync(Guid gateId, string approverUserId, string approverDisplayName, string notes, CancellationToken ct = default);
}

public record RunDetail(
    Guid RunId,
    bool IsActive,
    SdlcStage LastStage,
    IReadOnlyList<ArtifactSummary> Artifacts,
    IReadOnlyList<GateSummary> AllGates);

public record ArtifactSummary(
    Guid ArtifactId,
    string TypeName,
    SdlcStage Stage,
    ArtifactStatus Status,
    DateTimeOffset CreatedAt);

public class SdlcRunService(
    IArtifactStore artifactStore,
    IStageGateStore gateStore,
    IPipelineTelemetry telemetry,
    IPipelineRunner runner)
    : ISdlcRunService
{
    public async Task<IReadOnlyList<RunSummary>> GetActiveRunsAsync(CancellationToken ct = default)
    {
        // Get all run IDs from artifacts
        var runIds = await artifactStore.GetAllRunIdsAsync();
        var results = new List<RunSummary>();

        foreach (var runId in runIds)
        {
            var artifacts = await artifactStore.GetAllForRunAsync(runId);
            var gates = await gateStore.GetPendingForRunAsync(runId);

            if (!artifacts.Any())
                continue;

            var lastStage = artifacts.MaxBy(a => a.CreatedAt)?.Stage ?? SdlcStage.Research;
            var pendingGates = gates.Select(g => new GateSummary(g.GateId, g.Stage, g.Status, g.Notes)).ToList();

            var isActive = runner.IsRunActive(runId);
            results.Add(new RunSummary(runId, isActive, lastStage, pendingGates.AsReadOnly()));
        }

        return results.AsReadOnly();
    }

    public async Task<RunDetail?> GetRunDetailAsync(Guid runId, CancellationToken ct = default)
    {
        var artifacts = await artifactStore.GetAllForRunAsync(runId);
        if (!artifacts.Any())
            return null;

        var gates = new List<StageGate>();
        try
        {
            gates = (await gateStore.GetPendingForRunAsync(runId)).ToList();
        }
        catch
        {
            // Gates may not exist for this run yet
        }

        var allGates = gates.Select(g => new GateSummary(g.GateId, g.Stage, g.Status, g.Notes)).ToList();
        var artifactSummaries = artifacts.Select(a => new ArtifactSummary(
            a.ArtifactId,
            a.GetType().Name,
            a.Stage,
            a.Status,
            a.CreatedAt)).ToList();

        var lastStage = artifacts.MaxBy(a => a.CreatedAt)?.Stage ?? SdlcStage.Research;

        return new RunDetail(
            runId,
            runner.IsRunActive(runId),
            lastStage,
            artifactSummaries.AsReadOnly(),
            allGates.AsReadOnly());
    }

    public async Task ApproveGateAsync(Guid gateId, string approverUserId, string approverDisplayName, string? notes, CancellationToken ct = default)
    {
        var gate = await gateStore.GetAsync(gateId);
        if (gate is null)
            throw new KeyNotFoundException($"Gate {gateId} not found");

        await gateStore.ResolveAsync(gateId, GateDecision.Approved, notes, approverUserId, approverDisplayName);
        await telemetry.RecordGateApprovedAsync(gateId, approverUserId, ct);

        Task.Run(() => runner.ResumeGateAsync(gate.RunId, gateId, GateDecision.Approved, notes, ct));
    }

    public async Task RejectGateAsync(Guid gateId, string approverUserId, string approverDisplayName, string notes, CancellationToken ct = default)
    {
        var gate = await gateStore.GetAsync(gateId)
            ?? throw new KeyNotFoundException($"Gate {gateId} not found");

        await gateStore.ResolveAsync(gateId, GateDecision.Rejected, notes, approverUserId, approverDisplayName);
        await telemetry.RecordGateRejectedAsync(gateId, approverUserId, ct);

        Task.Run(() => runner.ResumeGateAsync(gate.RunId, gateId, GateDecision.Rejected, notes, ct));
    }
}
