using Dapper;
using FluentAssertions;
using Microsoft.Data.Sqlite;

namespace SDLC.Infrastructure.Tests;

[TestFixture, SingleThreaded]
public class MigrationRunnerTests
{
    private string _dbPath = null!;
    private IDbConnectionFactory _factory = null!;
    private MigrationRunner _runner = null!;

    [SetUp]
    public void SetUp()
    {
        _dbPath = Path.GetTempFileName();
        _factory = new SqlDbConnectionFactory($"Data Source={_dbPath}");
        _runner = new MigrationRunner(_factory);
    }

    [TearDown]
    public void TearDown()
    {
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { }
    }

    [Test]
    public async Task RunAsync_Creates_MigrationsTable()
    {
        await _runner.RunAsync();

        await using var conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync();
        var exists = await conn.QueryFirstOrDefaultAsync<int>(
            "SELECT count(*) FROM sqlite_master WHERE type='table' AND name='_migrations'");
        exists.Should().Be(1);
    }

    [Test]
    public async Task RunAsync_CreatesAllSchemaTables()
    {
        await _runner.RunAsync();

        await using var conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync();

        (await conn.QueryFirstOrDefaultAsync<int>(
            "SELECT count(*) FROM sqlite_master WHERE type='table' AND name='artifacts'")).Should().Be(1);
        (await conn.QueryFirstOrDefaultAsync<int>(
            "SELECT count(*) FROM sqlite_master WHERE type='table' AND name='runs'")).Should().Be(1);
        (await conn.QueryFirstOrDefaultAsync<int>(
            "SELECT count(*) FROM sqlite_master WHERE type='table' AND name='gates'")).Should().Be(1);
    }

    [Test]
    public async Task RunAsync_Idempotent_SecondCallDoesNotReapply()
    {
        await _runner.RunAsync();
        await _runner.RunAsync();

        await using var conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync();
        var count = await conn.QueryFirstOrDefaultAsync<int>("SELECT count(*) FROM _migrations");
        count.Should().Be(1);
    }

    [Test]
    public async Task RunAsync_PersistsVersionIn_MigrationsTable()
    {
        await _runner.RunAsync();

        await using var conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync();
        var version = await conn.QueryFirstOrDefaultAsync<int>("SELECT version FROM _migrations");
        version.Should().Be(0);
    }

    [Test]
    public async Task RunAsync_AppliedSchema_IsQueryable()
    {
        await _runner.RunAsync();

        await using var conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync();
        await conn.ExecuteAsync(
            "INSERT INTO artifacts (artifact_id, run_id, stage, status, file_path, created_at) " +
            "VALUES (@Id, @RunId, 'Research', 'Draft', '/tmp/test.md', @CreatedAt)",
            new
            {
                Id = Guid.NewGuid().ToString(),
                RunId = Guid.NewGuid().ToString(),
                CreatedAt = DateTimeOffset.UtcNow.ToString("o")
            });

        var count = await conn.QueryFirstOrDefaultAsync<int>("SELECT count(*) FROM artifacts");
        count.Should().Be(1);
    }
}
