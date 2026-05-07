using Dapper;
using Microsoft.Data.Sqlite;

namespace SDLC.Infrastructure;

public class RunStore : IRunStore
{
    private readonly string _connectionString;

    public RunStore(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task InitializeAsync()
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        await conn.ExecuteAsync(@"
            CREATE TABLE IF NOT EXISTS runs (
                run_id TEXT PRIMARY KEY,
                project_brief TEXT,
                current_stage TEXT NOT NULL,
                status TEXT NOT NULL DEFAULT 'Running',
                started_at TEXT NOT NULL
            )");
    }

    public async Task CreateRunAsync(Guid runId, string projectBrief, string startedAt)
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        await conn.ExecuteAsync(@"
            INSERT OR REPLACE INTO runs (run_id, project_brief, current_stage, status, started_at)
            VALUES (:runId, :projectBrief, 'Research', 'Running', :startedAt)",
            new { runId = runId.ToString(), projectBrief, startedAt });
    }

    public async Task UpdateStageAsync(Guid runId, string stage, string status)
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        await conn.ExecuteAsync(@"
            UPDATE runs SET current_stage = :stage, status = :status
            WHERE run_id = :runId",
            new { runId = runId.ToString(), stage, status });
    }

    public async Task<RunCheckpoint?> GetRunAsync(Guid runId)
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        var row = await conn.QueryFirstOrDefaultAsync(@"
            SELECT run_id, current_stage, status, started_at
            FROM runs WHERE run_id = :runId",
            new { runId = runId.ToString() });

        if (row == null) return null;

        return new RunCheckpoint(
            Guid.Parse(row.run_id),
            row.current_stage,
            row.status,
            DateTimeOffset.Parse(row.started_at));
    }

    public async Task<List<RunCheckpoint>> GetAllIncompleteAsync()
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        var rows = await conn.QueryAsync(@"
            SELECT run_id, current_stage, status, started_at
            FROM runs WHERE status IN ('Running', 'Blocked', 'Failed')
            ORDER BY started_at DESC", new { });

        return rows.Select(r => new RunCheckpoint(
            Guid.Parse(r.run_id),
            r.current_stage,
            r.status,
            DateTimeOffset.Parse(r.started_at))).ToList();
    }
}
