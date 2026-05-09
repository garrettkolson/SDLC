using System.Data.Common;
using Dapper;
using SDLC.Infrastructure.Migrations;

namespace SDLC.Infrastructure.Migrations;

public class Migration000_CreateInitialSchema : IMigration
{
    public int Version => 0;

    public async Task ApplyAsync(DbConnection conn)
    {
        await conn.ExecuteAsync(@"
            CREATE TABLE IF NOT EXISTS _migrations (
                version INTEGER PRIMARY KEY,
                applied_at TEXT NOT NULL
            )");

        await conn.ExecuteAsync(@"
            CREATE TABLE IF NOT EXISTS artifacts (
                artifact_id TEXT PRIMARY KEY,
                run_id TEXT NOT NULL,
                stage TEXT NOT NULL,
                status TEXT NOT NULL DEFAULT 'Draft',
                file_path TEXT NOT NULL,
                created_at TEXT NOT NULL
            )");

        await conn.ExecuteAsync(@"
            CREATE TABLE IF NOT EXISTS runs (
                run_id TEXT PRIMARY KEY,
                project_brief TEXT,
                current_stage TEXT NOT NULL,
                status TEXT NOT NULL DEFAULT 'Running',
                started_at TEXT NOT NULL
            )");

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
    }
}
