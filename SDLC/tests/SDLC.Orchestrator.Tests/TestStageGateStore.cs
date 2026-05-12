using SDLC.Contracts;
using SDLC.Infrastructure;

namespace SDLC.Orchestrator.Tests;

internal class TestStageGateStore : IStageGateStore
{
    public List<StageGate> Gates { get; } = new();

    public Task InitializeAsync() => Task.CompletedTask;
    public Task<StageGate> CreateGateAsync(SdlcArtifact artifact)
    {
        var gate = new StageGate { GateId = Guid.NewGuid(), RunId = artifact.RunId, Artifact = artifact };
        Gates.Add(gate);
        return Task.FromResult(gate);
    }
    public Task<StageGate?> GetAsync(Guid gateId) => Task.FromResult(Gates.FirstOrDefault(g => g.GateId == gateId));
    public Task ResolveAsync(Guid gateId, GateDecision decision, string? notes, string resolvedById, string resolvedByDisplay)
    {
        var gate = Gates.FirstOrDefault(g => g.GateId == gateId);
        gate!.Status = decision == GateDecision.Approved ? GateStatus.Approved : GateStatus.Rejected;
        gate.Notes = notes;
        gate.ResolvedById = resolvedById;
        gate.ResolvedByDisplay = resolvedByDisplay;
        gate.ResolvedAt = DateTimeOffset.UtcNow;
        return Task.CompletedTask;
    }
    public Task<List<StageGate>> GetPendingForRunAsync(Guid runId) => Task.FromResult(Gates.Where(g => g.RunId == runId && g.Status == GateStatus.Pending).ToList());
    public Task<List<StageGate>> GetAllPendingAsync() => Task.FromResult(Gates.Where(g => g.Status == GateStatus.Pending).ToList());
}
