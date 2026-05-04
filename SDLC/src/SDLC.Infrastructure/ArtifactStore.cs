using Dapper;
using Microsoft.Data.Sqlite;
using SDLC.Contracts;

namespace SDLC.Infrastructure;

public class ArtifactStore : IArtifactStore
{
    private readonly string _connectionString;
    private readonly string _basePath;

    public ArtifactStore(string connectionString, string basePath)
    {
        _connectionString = connectionString;
        _basePath = basePath;
    }

    public async Task InitializeAsync()
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        await conn.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS artifacts (
                artifact_id TEXT PRIMARY KEY,
                run_id TEXT NOT NULL,
                stage TEXT NOT NULL,
                status TEXT NOT NULL DEFAULT 'Draft',
                file_path TEXT NOT NULL,
                created_at TEXT NOT NULL
            )
            """);
    }

    public async Task SaveAsync(SdlcArtifact artifact)
    {
        var path = ArtifactPath(artifact);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, artifact.Content);

        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        await conn.ExecuteAsync("""
            INSERT OR REPLACE INTO artifacts
            (artifact_id, run_id, stage, status, file_path, created_at)
            VALUES (@ArtifactId, @RunId, @Stage, @Status, @FilePath, @CreatedAt)
            """, new
            {
                artifact.ArtifactId,
                artifact.RunId,
                Stage = artifact.Stage.ToString(),
                Status = artifact.Status.ToString(),
                FilePath = path,
                artifact.CreatedAt
            });
    }

    public async Task<T?> GetAsync<T>(Guid artifactId) where T : SdlcArtifact
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        var row = await conn.QueryFirstOrDefaultAsync("""
            SELECT artifact_id, file_path, stage, status, created_at FROM artifacts WHERE artifact_id = @Id
            """, new { Id = artifactId.ToString() });

        if (row == null) return null;

        var content = await File.ReadAllTextAsync(row.file_path);
        return (T)CreateArtifact(typeof(T), artifactId, content, row.stage, row.status, row.created_at);
    }

    public async Task<T?> GetLatestForRunAsync<T>(Guid runId) where T : SdlcArtifact
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        var stageStr = typeof(T).Name switch
        {
            "ResearchBrief" => "Research",
            "RequirementsSpec" => "Requirements",
            "ArchitectureRecord" => "Design",
            "BuildResult" => "Build",
            "LearnReport" => "Learn",
            _ => throw new NotSupportedException()
        };
        var row = await conn.QueryFirstOrDefaultAsync("""
            SELECT artifact_id, file_path, stage, status, created_at FROM artifacts
            WHERE run_id = @RunId AND stage = @Stage
            ORDER BY created_at DESC LIMIT 1
            """, new { RunId = runId.ToString(), Stage = stageStr });

        if (row == null) return null;

        var content = await File.ReadAllTextAsync(row.file_path);
        return (T)CreateArtifact(typeof(T), Guid.Parse(row.artifact_id), content, row.stage, row.status, row.created_at);
    }

    public async Task UpdateStatusAsync(Guid artifactId, ArtifactStatus status)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        await conn.ExecuteAsync("""
            UPDATE artifacts SET status = @Status WHERE artifact_id = @Id
            """, new { Id = artifactId.ToString(), Status = status.ToString() });
    }

    public async Task UpdateContentAsync(Guid artifactId, string content)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        var row = await conn.QueryFirstOrDefaultAsync<string>("SELECT file_path FROM artifacts WHERE artifact_id = @Id",
            new { Id = artifactId.ToString() });

        if (row != null)
        {
            await File.WriteAllTextAsync(row, content);
        }

        await conn.ExecuteAsync("""
            UPDATE artifacts SET status = 'Draft' WHERE artifact_id = @Id
            """, new { Id = artifactId.ToString() });
    }

    public async Task<List<SdlcArtifact>> GetAllForRunAsync(Guid runId)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        var rows = await conn.QueryAsync("""
            SELECT artifact_id, file_path, stage, status, created_at FROM artifacts
            WHERE run_id = @RunId ORDER BY
                CASE stage
                    WHEN 'Research' THEN 1
                    WHEN 'Requirements' THEN 2
                    WHEN 'Design' THEN 3
                    WHEN 'Build' THEN 4
                    WHEN 'Learn' THEN 5
                END
            """, new { RunId = runId.ToString() });

        var result = new List<SdlcArtifact>();
        foreach (var row in rows)
        {
            var content = await File.ReadAllTextAsync(row.file_path);
            var artifactId = Guid.Parse(row.artifact_id);
            var stage = (SdlcStage)Enum.Parse(typeof(SdlcStage), row.stage);
            var status = (ArtifactStatus)Enum.Parse(typeof(ArtifactStatus), row.status);
            var createdAt = DateTimeOffset.Parse(row.created_at);

            SdlcArtifact artifact = row.stage switch
            {
                "Research" => (SdlcArtifact)new ResearchBrief { ArtifactId = artifactId, Content = content, Stage = stage, Status = status, CreatedAt = createdAt, RunId = runId },
                "Requirements" => (SdlcArtifact)new RequirementsSpec { ArtifactId = artifactId, Content = content, Stage = stage, Status = status, CreatedAt = createdAt, RunId = runId },
                "Design" => (SdlcArtifact)new ArchitectureRecord { ArtifactId = artifactId, Content = content, Stage = stage, Status = status, CreatedAt = createdAt, RunId = runId },
                "Build" => (SdlcArtifact)new BuildResult { ArtifactId = artifactId, Stage = stage, Status = status, CreatedAt = createdAt, RunId = runId },
                "Learn" => (SdlcArtifact)new LearnReport { ArtifactId = artifactId, Stage = stage, Status = status, CreatedAt = createdAt, RunId = runId },
                _ => throw new NotSupportedException($"Unknown stage: {row.stage}")
            };
            result.Add(artifact);
        }
        return result;
    }

    private string ArtifactPath(SdlcArtifact a) => Path.Combine(_basePath, a.RunId.ToString(), $"{a.Stage}.md");

    private SdlcArtifact CreateArtifact(Type type, Guid artifactId, string content, string stage, string status, string createdAt)
    {
        return type.Name switch
        {
            "ResearchBrief" => (SdlcArtifact)new ResearchBrief { ArtifactId = artifactId, Content = content, Stage = SdlcStage.Research, Status = (ArtifactStatus)Enum.Parse(typeof(ArtifactStatus), status), CreatedAt = DateTimeOffset.Parse(createdAt) },
            "RequirementsSpec" => (SdlcArtifact)new RequirementsSpec { ArtifactId = artifactId, Content = content, Stage = SdlcStage.Requirements, Status = (ArtifactStatus)Enum.Parse(typeof(ArtifactStatus), status), CreatedAt = DateTimeOffset.Parse(createdAt) },
            "ArchitectureRecord" => (SdlcArtifact)new ArchitectureRecord { ArtifactId = artifactId, Content = content, Stage = SdlcStage.Design, Status = (ArtifactStatus)Enum.Parse(typeof(ArtifactStatus), status), CreatedAt = DateTimeOffset.Parse(createdAt) },
            "BuildResult" => (SdlcArtifact)new BuildResult { ArtifactId = artifactId, Stage = SdlcStage.Build, Status = (ArtifactStatus)Enum.Parse(typeof(ArtifactStatus), status), CreatedAt = DateTimeOffset.Parse(createdAt) },
            "LearnReport" => (SdlcArtifact)new LearnReport { ArtifactId = artifactId, Stage = SdlcStage.Learn, Status = (ArtifactStatus)Enum.Parse(typeof(ArtifactStatus), status), CreatedAt = DateTimeOffset.Parse(createdAt) },
            _ => throw new NotSupportedException($"Unknown artifact type: {type.Name}")
        };
    }
}
