using SDLC.Contracts;

namespace SDLC.Infrastructure;

public interface IArtifactStore
{
    Task SaveAsync(SdlcArtifact artifact);
    Task<T?> GetAsync<T>(Guid artifactId) where T : SdlcArtifact;
    Task<T?> GetLatestForRunAsync<T>(Guid runId) where T : SdlcArtifact;
    Task UpdateStatusAsync(Guid artifactId, ArtifactStatus status);
    Task UpdateContentAsync(Guid artifactId, string content);
    Task<List<SdlcArtifact>> GetAllForRunAsync(Guid runId);
    Task<List<Guid>> GetAllRunIdsAsync();
}

public interface IStageGateStore
{
    Task<StageGate> CreateGateAsync(SdlcArtifact artifact);
    Task<StageGate?> GetAsync(Guid gateId);
    Task ResolveAsync(Guid gateId, GateDecision decision, string? notes, string resolvedById, string resolvedByDisplay);
    Task<List<StageGate>> GetPendingForRunAsync(Guid runId);
    Task<List<StageGate>> GetAllPendingAsync();
}

public interface IRunStore
{
    Task CreateRunAsync(Guid runId, string projectBrief, string startedAt);
    Task UpdateStageAsync(Guid runId, string stage, string status);
    Task<RunCheckpoint?> GetRunAsync(Guid runId);
    Task<List<RunCheckpoint>> GetAllIncompleteAsync();
    Task CancelRunAsync(Guid runId);
}

public record RunCheckpoint(Guid RunId, string CurrentStage, string Status, DateTimeOffset StartedAt);

public class StageGate
{
    public Guid GateId { get; init; } = Guid.NewGuid();
    public Guid RunId { get; init; }
    public SdlcStage Stage { get; init; }
    public GateStatus Status { get; set; } = GateStatus.Pending;
    public string? Notes { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
    public string? ResolvedById { get; set; }
    public string? ResolvedByDisplay { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public SdlcArtifact? Artifact { get; set; }
}
