using Dapper;
using Microsoft.Data.Sqlite;

namespace SDLC.Infrastructure;

public class RunStore : IRunStore
{
    private readonly IDbConnectionFactory _factory;

    public RunStore(IDbConnectionFactory factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync()
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync();
        await conn.ExecuteAsync(@"
            CREATE TABLE IF NOT EXISTS runs (
                run_id TEXT PRIMARY KEY,
                project_brief TEXT,
                current_stage TEXT NOT NULL,
                status TEXT NOT NULL DEFAULT 'Running',
                started_at TEXT NOT NULL
            )");
        await conn.ExecuteAsync("PRAGMA journal_mode = WAL;");
        await conn.ExecuteAsync("PRAGMA synchronous = NORMAL;");
    }

    public async Task CreateRunAsync(Guid runId, string projectBrief, string startedAt)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync();
        await conn.ExecuteAsync(@"
            INSERT OR REPLACE INTO runs (run_id, project_brief, current_stage, status, started_at)
            VALUES (:runId, :projectBrief, 'Research', 'Running', :startedAt)",
            new { runId = runId.ToString(), projectBrief, startedAt });
    }

    public async Task UpdateStageAsync(Guid runId, string stage, string status)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync();
        await conn.ExecuteAsync(@"
            UPDATE runs SET current_stage = :stage, status = :status
            WHERE run_id = :runId",
            new { runId = runId.ToString(), stage, status });
    }

    public async Task<RunCheckpoint?> GetRunAsync(Guid runId)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync();
        var row = await conn.QueryFirstOrDefaultAsync(@"
            SELECT run_id, project_brief, current_stage, status, started_at
            FROM runs WHERE run_id = :runId",
            new { runId = runId.ToString() });

        if (row == null) return null;

        return new RunCheckpoint(
            Guid.Parse(row.run_id),
            row.current_stage,
            row.status,
            DateTimeOffset.Parse(row.started_at),
            row.project_brief);
    }

    public async Task<List<RunCheckpoint>> GetAllIncompleteAsync()
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync();
        var rows = await conn.QueryAsync(@"
            SELECT run_id, project_brief, current_stage, status, started_at
            FROM runs WHERE status IN ('Running', 'Blocked', 'Failed')
            ORDER BY started_at DESC", new { });

        return rows.Select(r => new RunCheckpoint(
            Guid.Parse(r.run_id),
            r.current_stage,
            r.status,
            DateTimeOffset.Parse(r.started_at),
            r.project_brief)).ToList();
    }

    public async Task CancelRunAsync(Guid runId)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync();
        await conn.ExecuteAsync(@"
            UPDATE runs SET status = 'Cancelled'
            WHERE run_id = @runId",
            new { runId = runId.ToString() });
    }
}
