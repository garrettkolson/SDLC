using Dapper;
using SDLC.Contracts;

namespace SDLC.Infrastructure;

public class StageGateStore : IStageGateStore
{
    private readonly IDbConnectionFactory _factory;

    public StageGateStore(IDbConnectionFactory factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync()
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync();
        await conn.ExecuteAsync(@"
            CREATE TABLE IF NOT EXISTS gates (
                gate_id TEXT PRIMARY KEY,
                run_id TEXT NOT NULL,
                stage TEXT NOT NULL,
                status TEXT NOT NULL DEFAULT 'Pending',
                notes TEXT,
                resolved_at TEXT,
                resolved_by_user_id TEXT,
                resolved_by_display TEXT,
                artifact_content TEXT,
                artifact_type TEXT,
                created_at TEXT NOT NULL
            )");
        await conn.ExecuteAsync("PRAGMA journal_mode = WAL;");
        await conn.ExecuteAsync("PRAGMA synchronous = NORMAL;");
    }

    public async Task<StageGate> CreateGateAsync(SdlcArtifact artifact)
    {
        var gate = new StageGate
        {
            RunId = artifact.RunId,
            Stage = artifact.Stage,
            Status = GateStatus.Pending,
            Artifact = artifact
        };

        await using var conn = _factory.Create();
        await conn.OpenAsync();
        await conn.ExecuteAsync(@"
            INSERT INTO gates (gate_id, run_id, stage, status, artifact_content, artifact_type, created_at)
            VALUES (@gateId, @runId, @stage, @status, @artifactContent, @artifactType, @createdAt)",
            new
            {
                gateId = gate.GateId.ToString(),
                runId = gate.RunId.ToString(),
                stage = gate.Stage.ToString(),
                status = gate.Status.ToString(),
                artifactContent = (object?)artifact.Content ?? "",
                artifactType = artifact.GetType().Name,
                createdAt = DateTimeOffset.UtcNow.ToString("o")
            });

        return gate;
    }

    public async Task<StageGate?> GetAsync(Guid gateId)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync();
        var row = await conn.QueryFirstOrDefaultAsync<dynamic>(@"
            SELECT gate_id, run_id, stage, status, notes, resolved_at, resolved_by_user_id, resolved_by_display, artifact_content, artifact_type, created_at
            FROM gates WHERE gate_id = @id",
            new { id = gateId.ToString() });

        if (row == null) return null;

        return ReadGate(row);
    }

    public async Task ResolveAsync(Guid gateId, GateDecision decision, string? notes, string resolvedById, string resolvedByDisplay)
    {
        var status = decision == GateDecision.Approved
            ? GateStatus.Approved.ToString()
            : GateStatus.Rejected.ToString();

        await using var conn = _factory.Create();
        await conn.OpenAsync();
        await conn.ExecuteAsync(@"
            UPDATE gates SET status = @status, notes = @notes, resolved_at = @resolvedAt,
                resolved_by_user_id = @resolvedById, resolved_by_display = @resolvedByDisplay
            WHERE gate_id = @id",
            new
            {
                status,
                notes = (object?)notes ?? DBNull.Value,
                resolvedAt = DateTimeOffset.UtcNow.ToString("o"),
                resolvedById = (object?)resolvedById ?? DBNull.Value,
                resolvedByDisplay = (object?)resolvedByDisplay ?? DBNull.Value,
                id = gateId.ToString()
            });
    }

    public async Task<List<StageGate>> GetPendingForRunAsync(Guid runId)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync();
        var rows = await conn.QueryAsync<dynamic>(@"
            SELECT gate_id, run_id, stage, status, notes, resolved_at, resolved_by_user_id, resolved_by_display, artifact_content, artifact_type, created_at
            FROM gates WHERE run_id = @runId AND status = 'Pending'",
            new { runId = runId.ToString() });

        return rows.Select(ReadGate).ToList();
    }

    public async Task<List<StageGate>> GetAllPendingAsync()
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync();
        var rows = await conn.QueryAsync<dynamic>(@"
            SELECT gate_id, run_id, stage, status, notes, resolved_at, resolved_by_user_id, resolved_by_display, artifact_content, artifact_type, created_at
            FROM gates WHERE status = 'Pending'");

        return rows.Select(ReadGate).ToList();
    }

    private static StageGate ReadGate(dynamic row)
    {
        string artifactType = row.artifact_type;
        SdlcArtifact? artifact = artifactType switch
        {
            "ResearchBrief" => new ResearchBrief { Content = row.artifact_content as string ?? "" },
            "RequirementsSpec" => new RequirementsSpec { Content = row.artifact_content as string ?? "" },
            "ArchitectureRecord" => new ArchitectureRecord { Content = row.artifact_content as string ?? "" },
            "BuildResult" => new BuildResult(),
            "LearnReport" => new LearnReport(),
            _ => null
        };

        return new StageGate
        {
            GateId = Guid.Parse(row.gate_id),
            RunId = Guid.Parse(row.run_id),
            Stage = (SdlcStage)Enum.Parse(typeof(SdlcStage), row.stage),
            Status = (GateStatus)Enum.Parse(typeof(GateStatus), row.status),
            Notes = row.notes as string,
            ResolvedAt = string.IsNullOrEmpty(row.resolved_at) ? null : DateTimeOffset.Parse(row.resolved_at),
            ResolvedById = string.IsNullOrEmpty(row.resolved_by_user_id) ? null : row.resolved_by_user_id as string,
            ResolvedByDisplay = string.IsNullOrEmpty(row.resolved_by_display) ? null : row.resolved_by_display as string,
            CreatedAt = DateTimeOffset.Parse(row.created_at),
            Artifact = artifact
        };
    }
}
