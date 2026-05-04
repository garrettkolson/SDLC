using Dapper;
using Microsoft.Data.Sqlite;
using SDLC.Contracts;

namespace SDLC.Infrastructure;

public class StageGateStore
{
    private readonly string _connectionString;

    public StageGateStore(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task InitializeAsync()
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        await conn.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS gates (
                gate_id TEXT PRIMARY KEY,
                run_id TEXT NOT NULL,
                stage TEXT NOT NULL,
                status TEXT NOT NULL DEFAULT 'Pending',
                notes TEXT,
                resolved_at TEXT,
                artifact_content TEXT,
                artifact_type TEXT,
                created_at TEXT NOT NULL
            )
            """);
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

        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        await conn.ExecuteAsync("""
            INSERT INTO gates (gate_id, run_id, stage, status, artifact_content, artifact_type, created_at)
            VALUES (@GateId, @RunId, @Stage, @Status, @ArtifactContent, @ArtifactType, @CreatedAt)
            """, new
            {
                GateId = gate.GateId,
                gate.RunId,
                Stage = gate.Stage.ToString(),
                Status = gate.Status.ToString(),
                ArtifactContent = artifact.Content,
                ArtifactType = artifact.GetType().Name,
                CreatedAt = DateTimeOffset.UtcNow
            });

        return gate;
    }

    public async Task<StageGate?> GetAsync(Guid gateId)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        var row = await conn.QueryFirstOrDefaultAsync("""
            SELECT * FROM gates WHERE gate_id = @Id
            """, new { Id = gateId.ToString() });

        if (row == null) return null;

        return CreateGate(row);
    }

    public async Task ResolveAsync(Guid gateId, GateDecision decision, string? notes)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        await conn.ExecuteAsync("""
            UPDATE gates
            SET status = @Status, notes = @Notes, resolved_at = @ResolvedAt
            WHERE gate_id = @Id
            """, new
            {
                Id = gateId.ToString(),
                Status = decision.ToString(),
                Notes = notes,
                ResolvedAt = DateTimeOffset.UtcNow
            });
    }

    public async Task<List<StageGate>> GetPendingForRunAsync(Guid runId)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        var rows = await conn.QueryAsync("""
            SELECT * FROM gates WHERE run_id = @RunId AND status = 'Pending'
            """, new { RunId = runId.ToString() });

        return [.. rows.Select(CreateGate)];
    }

    private StageGate CreateGate(dynamic row)
    {
        SdlcArtifact? artifact = row.artifact_type switch
        {
            "ResearchBrief" => new ResearchBrief { Content = row.artifact_content },
            "RequirementsSpec" => new RequirementsSpec { Content = row.artifact_content },
            "ArchitectureRecord" => new ArchitectureRecord { Content = row.artifact_content },
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
            Notes = row.notes,
            ResolvedAt = row.resolved_at != null ? DateTimeOffset.Parse(row.resolved_at) : null,
            Artifact = artifact
        };
    }
}
