using SDLC.Contracts;

namespace SDLC.Infrastructure;

public class StageGateStore : IStageGateStore
{
    private readonly string _connectionString;

    public StageGateStore(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task InitializeAsync()
    {
        await using var conn = new Microsoft.Data.Sqlite.SqliteConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new Microsoft.Data.Sqlite.SqliteCommand(@"
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
            )", conn);
        await cmd.ExecuteNonQueryAsync();

        // WAL + synchronous pragmas
        await using var pragmaCmd1 = new Microsoft.Data.Sqlite.SqliteCommand("PRAGMA journal_mode = WAL;", conn);
        await pragmaCmd1.ExecuteNonQueryAsync();
        await using var pragmaCmd2 = new Microsoft.Data.Sqlite.SqliteCommand("PRAGMA synchronous = NORMAL;", conn);
        await pragmaCmd2.ExecuteNonQueryAsync();
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

        await using var conn = new Microsoft.Data.Sqlite.SqliteConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new Microsoft.Data.Sqlite.SqliteCommand(@"
            INSERT INTO gates (gate_id, run_id, stage, status, artifact_content, artifact_type, created_at)
            VALUES (@gate_id, @run_id, @stage, @status, @artifact_content, @artifact_type, @created_at)", conn);
        cmd.Parameters.AddWithValue("@gate_id", gate.GateId.ToString());
        cmd.Parameters.AddWithValue("@run_id", gate.RunId.ToString());
        cmd.Parameters.AddWithValue("@stage", gate.Stage.ToString());
        cmd.Parameters.AddWithValue("@status", gate.Status.ToString());
        cmd.Parameters.AddWithValue("@artifact_content", (object?)artifact.Content ?? "");
        cmd.Parameters.AddWithValue("@artifact_type", artifact.GetType().Name);
        cmd.Parameters.AddWithValue("@created_at", DateTimeOffset.UtcNow.ToString("o"));
        await cmd.ExecuteNonQueryAsync();

        return gate;
    }

    public async Task<StageGate?> GetAsync(Guid gateId)
    {
        await using var conn = new Microsoft.Data.Sqlite.SqliteConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new Microsoft.Data.Sqlite.SqliteCommand(@"
            SELECT gate_id, run_id, stage, status, notes, resolved_at, resolved_by_user_id, resolved_by_display, artifact_content, artifact_type, created_at
            FROM gates WHERE gate_id = @id", conn);
        cmd.Parameters.AddWithValue("@id", gateId.ToString());
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;

        return ReadGate(reader);
    }

    public async Task ResolveAsync(Guid gateId, GateDecision decision, string? notes, string resolvedById, string resolvedByDisplay)
    {
        var status = decision == GateDecision.Approved
            ? GateStatus.Approved.ToString()
            : GateStatus.Rejected.ToString();

        await using var conn = new Microsoft.Data.Sqlite.SqliteConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new Microsoft.Data.Sqlite.SqliteCommand(@"
            UPDATE gates SET status = @status, notes = @notes, resolved_at = @resolved_at,
                resolved_by_user_id = @resolvedById, resolved_by_display = @resolvedByDisplay
            WHERE gate_id = @id", conn);
        cmd.Parameters.AddWithValue("@status", status);
        cmd.Parameters.AddWithValue("@notes", (object?)notes ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@resolved_at", DateTimeOffset.UtcNow.ToString("o"));
        cmd.Parameters.AddWithValue("@resolvedById", (object?)resolvedById ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@resolvedByDisplay", (object?)resolvedByDisplay ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@id", gateId.ToString());
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<List<StageGate>> GetPendingForRunAsync(Guid runId)
    {
        await using var conn = new Microsoft.Data.Sqlite.SqliteConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new Microsoft.Data.Sqlite.SqliteCommand(@"
            SELECT gate_id, run_id, stage, status, notes, resolved_at, resolved_by_user_id, resolved_by_display, artifact_content, artifact_type, created_at
            FROM gates WHERE run_id = @run_id AND status = 'Pending'", conn);
        cmd.Parameters.AddWithValue("@run_id", runId.ToString());
        await using var reader = await cmd.ExecuteReaderAsync();
        var gates = new List<StageGate>();
        while (await reader.ReadAsync())
            gates.Add(ReadGate(reader));
        return gates;
    }

    public async Task<List<StageGate>> GetAllPendingAsync()
    {
        await using var conn = new Microsoft.Data.Sqlite.SqliteConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new Microsoft.Data.Sqlite.SqliteCommand(@"
            SELECT gate_id, run_id, stage, status, notes, resolved_at, resolved_by_user_id, resolved_by_display, artifact_content, artifact_type, created_at
            FROM gates WHERE status = 'Pending'", conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        var gates = new List<StageGate>();
        while (await reader.ReadAsync())
            gates.Add(ReadGate(reader));
        return gates;
    }

    private static StageGate ReadGate(Microsoft.Data.Sqlite.SqliteDataReader reader)
    {
        string artifactType = reader.GetString(reader.GetOrdinal("artifact_type"));
        SdlcArtifact? artifact = artifactType switch
        {
            "ResearchBrief" => new ResearchBrief { Content = reader["artifact_content"] as string ?? "" },
            "RequirementsSpec" => new RequirementsSpec { Content = reader["artifact_content"] as string ?? "" },
            "ArchitectureRecord" => new ArchitectureRecord { Content = reader["artifact_content"] as string ?? "" },
            "BuildResult" => new BuildResult(),
            "LearnReport" => new LearnReport(),
            _ => null
        };

        int resolvedIdx = reader.GetOrdinal("resolved_at");
        int resolvedByIdIdx = reader.GetOrdinal("resolved_by_user_id");
        int resolvedByDisplayIdx = reader.GetOrdinal("resolved_by_display");
        int createdIdx = reader.GetOrdinal("created_at");

        return new StageGate
        {
            GateId = Guid.Parse(reader.GetString(reader.GetOrdinal("gate_id"))),
            RunId = Guid.Parse(reader.GetString(reader.GetOrdinal("run_id"))),
            Stage = (SdlcStage)Enum.Parse(typeof(SdlcStage), reader.GetString(reader.GetOrdinal("stage"))),
            Status = (GateStatus)Enum.Parse(typeof(GateStatus), reader.GetString(reader.GetOrdinal("status"))),
            Notes = reader["notes"] as string,
            ResolvedAt = reader.IsDBNull(resolvedIdx) ? null : DateTimeOffset.Parse(reader.GetString(resolvedIdx)),
            ResolvedById = reader.IsDBNull(resolvedByIdIdx) ? null : reader.GetString(resolvedByIdIdx),
            ResolvedByDisplay = reader.IsDBNull(resolvedByDisplayIdx) ? null : reader.GetString(resolvedByDisplayIdx),
            CreatedAt = DateTimeOffset.Parse(reader.GetString(createdIdx)),
            Artifact = artifact
        };
    }
}
