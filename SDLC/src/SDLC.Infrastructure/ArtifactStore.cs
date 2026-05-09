using Dapper;
using Microsoft.Data.Sqlite;
using SDLC.Contracts;

namespace SDLC.Infrastructure;

public class ArtifactStore : IArtifactStore
{
    private readonly IDbConnectionFactory _factory;
    private readonly string _basePath;

    public ArtifactStore(IDbConnectionFactory factory, string basePath)
    {
        _factory = factory;
        _basePath = basePath;
    }

    public async Task InitializeAsync()
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync();
        await conn.ExecuteAsync(@"
            CREATE TABLE IF NOT EXISTS artifacts (
                artifact_id TEXT PRIMARY KEY,
                run_id TEXT NOT NULL,
                stage TEXT NOT NULL,
                status TEXT NOT NULL DEFAULT 'Draft',
                file_path TEXT NOT NULL,
                created_at TEXT NOT NULL
            )");
        await conn.ExecuteAsync("PRAGMA journal_mode = WAL;");
        await conn.ExecuteAsync("PRAGMA synchronous = NORMAL;");
    }

    public async Task SaveAsync(SdlcArtifact artifact)
    {
        var path = ArtifactPath(artifact);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, artifact.Content ?? "");

        await using var conn = _factory.Create();
        await conn.OpenAsync();
        await conn.ExecuteAsync(@"
            INSERT OR REPLACE INTO artifacts
            (artifact_id, run_id, stage, status, file_path, created_at)
            VALUES (:ArtifactId, :RunId, :Stage, :Status, :FilePath, :CreatedAt)",
            new
            {
                ArtifactId = artifact.ArtifactId.ToString(),
                RunId = artifact.RunId.ToString(),
                Stage = artifact.Stage.ToString(),
                Status = artifact.Status.ToString(),
                FilePath = path,
                CreatedAt = artifact.CreatedAt.ToString("o")
            });
    }

    public async Task<T?> GetAsync<T>(Guid artifactId) where T : SdlcArtifact
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync();
        var row = await conn.QueryFirstOrDefaultAsync<dynamic>(@"
            SELECT artifact_id, run_id, file_path, stage, status, created_at
            FROM artifacts WHERE artifact_id = :Id",
            new { Id = artifactId.ToString() });

        if (row == null) return null;

        var content = await File.ReadAllTextAsync(row.file_path);
        var runId = Guid.Parse(row.run_id);
        return (T)CreateArtifact(typeof(T), artifactId, content, runId, row.stage, row.status, row.created_at);
    }

    public async Task<T?> GetLatestForRunAsync<T>(Guid runId) where T : SdlcArtifact
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync();
        var stageStr = GetStageName(typeof(T));
        var row = await conn.QueryFirstOrDefaultAsync<dynamic>(@"
            SELECT artifact_id, run_id, file_path, stage, status, created_at
            FROM artifacts
            WHERE run_id = :RunId AND stage = :Stage
            ORDER BY created_at DESC LIMIT 1",
            new { RunId = runId.ToString(), Stage = stageStr });

        if (row == null) return null;

        var content = await File.ReadAllTextAsync(row.file_path);
        var runIdFromDb = Guid.Parse(row.run_id);
        return (T)CreateArtifact(typeof(T), Guid.Parse(row.artifact_id), content, runIdFromDb, row.stage, row.status, row.created_at);
    }

    public async Task UpdateStatusAsync(Guid artifactId, ArtifactStatus status)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync();
        await conn.ExecuteAsync(@"
            UPDATE artifacts SET status = :Status
            WHERE artifact_id = :Id",
            new { Id = artifactId.ToString(), Status = status.ToString() });
    }

    public async Task UpdateContentAsync(Guid artifactId, string content)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync();
        using var tx = await conn.BeginTransactionAsync();

        var row = await conn.QueryFirstOrDefaultAsync<string>(
            "SELECT file_path FROM artifacts WHERE artifact_id = :Id",
            new { Id = artifactId.ToString() }, tx);

        if (row != null)
            await File.WriteAllTextAsync(row, content);

        // Content edited → needs re-approval; reset to PendingReview (not Draft)
        await conn.ExecuteAsync(@"
            UPDATE artifacts SET status = 'PendingReview' WHERE artifact_id = :Id",
            new { Id = artifactId.ToString() }, tx);

        await tx.CommitAsync();
    }

    public async Task<List<SdlcArtifact>> GetAllForRunAsync(Guid runId)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync();
        var rows = await conn.QueryAsync<dynamic>(@"
            SELECT artifact_id, file_path, stage, status, created_at
            FROM artifacts
            WHERE run_id = :RunId
            ORDER BY
                CASE stage
                    WHEN 'Research' THEN 1
                    WHEN 'Requirements' THEN 2
                    WHEN 'Design' THEN 3
                    WHEN 'Build' THEN 4
                    WHEN 'Learn' THEN 5
                END",
            new { RunId = runId.ToString() });

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
                "Research" => new ResearchBrief { ArtifactId = artifactId, Content = content, Stage = stage, Status = status, CreatedAt = createdAt, RunId = runId },
                "Requirements" => new RequirementsSpec { ArtifactId = artifactId, Content = content, Stage = stage, Status = status, CreatedAt = createdAt, RunId = runId },
                "Design" => new ArchitectureRecord { ArtifactId = artifactId, Content = content, Stage = stage, Status = status, CreatedAt = createdAt, RunId = runId },
                "Build" => new BuildResult { ArtifactId = artifactId, Stage = stage, Status = status, CreatedAt = createdAt, RunId = runId },
                "Learn" => new LearnReport { ArtifactId = artifactId, Stage = stage, Status = status, CreatedAt = createdAt, RunId = runId },
                _ => throw new NotSupportedException($"Unknown stage: {row.stage}")
            };
            result.Add(artifact);
        }
        return result;
    }

    public async Task<List<Guid>> GetAllRunIdsAsync()
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync();
        var rows = await conn.QueryAsync<string>(
            "SELECT DISTINCT run_id FROM artifacts ORDER BY created_at DESC");
        return rows.Select(Guid.Parse).ToList();
    }

    private string ArtifactPath(SdlcArtifact a) =>
        Path.Combine(_basePath, a.RunId.ToString(), $"{a.Stage}-{a.ArtifactId:N}.md");

    private string GetStageName(Type type) => type.Name switch
    {
        "ResearchBrief" => "Research",
        "RequirementsSpec" => "Requirements",
        "ArchitectureRecord" => "Design",
        "BuildResult" => "Build",
        "LearnReport" => "Learn",
        _ => throw new NotSupportedException($"Unknown artifact type: {type.Name}")
    };

    private SdlcArtifact CreateArtifact(Type type, Guid artifactId, string content, Guid runId, string stage, string status, string createdAt)
    {
        var stageEnum = (SdlcStage)Enum.Parse(typeof(SdlcStage), stage);
        var statusEnum = (ArtifactStatus)Enum.Parse(typeof(ArtifactStatus), status);
        var createdAtOffset = DateTimeOffset.Parse(createdAt);

        SdlcArtifact artifact = type.Name switch
        {
            "ResearchBrief" => new ResearchBrief { ArtifactId = artifactId, Content = content, RunId = runId, Stage = SdlcStage.Research, Status = statusEnum, CreatedAt = createdAtOffset },
            "RequirementsSpec" => new RequirementsSpec { ArtifactId = artifactId, Content = content, RunId = runId, Stage = SdlcStage.Requirements, Status = statusEnum, CreatedAt = createdAtOffset },
            "ArchitectureRecord" => new ArchitectureRecord { ArtifactId = artifactId, Content = content, RunId = runId, Stage = SdlcStage.Design, Status = statusEnum, CreatedAt = createdAtOffset },
            "BuildResult" => new BuildResult { ArtifactId = artifactId, Stage = SdlcStage.Build, Status = statusEnum, CreatedAt = createdAtOffset, RunId = runId },
            "LearnReport" => new LearnReport { ArtifactId = artifactId, Stage = SdlcStage.Learn, Status = statusEnum, CreatedAt = createdAtOffset, RunId = runId },
            _ => throw new NotSupportedException($"Unknown artifact type: {type.Name}")
        };
        return artifact;
    }
}
