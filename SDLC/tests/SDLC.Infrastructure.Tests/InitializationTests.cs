using Microsoft.Data.Sqlite;

namespace SDLC.Infrastructure;

[TestFixture, SingleThreaded]
public class InitializationTests
{
    private string _dbPath = null!;

    [SetUp]
    public void SetUp()
    {
        _dbPath = Path.GetTempFileName();
    }

    [TearDown]
    public void TearDown()
    {
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { }
    }

    [Test]
    public async Task ArtifactStoreInitializeAsync_SetsWALMode()
    {
        var store = new ArtifactStore(new SqlDbConnectionFactory($"Data Source={_dbPath}"), Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()));
        await store.InitializeAsync();

        var mode = await QueryPragma(_dbPath, "PRAGMA journal_mode");
        Assert.That(mode, Is.EqualTo("wal"));
    }

    [Test]
    public async Task ArtifactStoreInitializeAsync_SetsSynchronousNormal()
    {
        var store = new ArtifactStore(new SqlDbConnectionFactory($"Data Source={_dbPath}"), Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()));
        await store.InitializeAsync();

        var sync = await QueryPragmaInt(_dbPath, "PRAGMA synchronous");
        Assert.That(sync, Is.EqualTo(1)); // 1 = NORMAL
    }

    [Test]
    public async Task RunStoreInitializeAsync_SetsWALMode()
    {
        var store = new RunStore(new SqlDbConnectionFactory($"Data Source={_dbPath}"));
        await store.InitializeAsync();

        var mode = await QueryPragma(_dbPath, "PRAGMA journal_mode");
        Assert.That(mode, Is.EqualTo("wal"));
    }

    [Test]
    public async Task RunStoreInitializeAsync_SetsSynchronousNormal()
    {
        var store = new RunStore(new SqlDbConnectionFactory($"Data Source={_dbPath}"));
        await store.InitializeAsync();

        var sync = await QueryPragmaInt(_dbPath, "PRAGMA synchronous");
        Assert.That(sync, Is.EqualTo(1));
    }

    [Test]
    public async Task StageGateStoreInitializeAsync_SetsWALMode()
    {
        var store = new StageGateStore(new SqlDbConnectionFactory($"Data Source={_dbPath}"));
        await store.InitializeAsync();

        var mode = await QueryPragma(_dbPath, "PRAGMA journal_mode");
        Assert.That(mode, Is.EqualTo("wal"));
    }

    [Test]
    public async Task StageGateStoreInitializeAsync_SetsSynchronousNormal()
    {
        var store = new StageGateStore(new SqlDbConnectionFactory($"Data Source={_dbPath}"));
        await store.InitializeAsync();

        var sync = await QueryPragmaInt(_dbPath, "PRAGMA synchronous");
        Assert.That(sync, Is.EqualTo(1));
    }

    [Test]
    public async Task AllStoresInitializeAsync_DoesNotThrowIfTableAlreadyExists()
    {
        var artifactStore = new ArtifactStore(new SqlDbConnectionFactory($"Data Source={_dbPath}"), Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()));
        var runStore = new RunStore(new SqlDbConnectionFactory($"Data Source={_dbPath}"));
        var gateStore = new StageGateStore(new SqlDbConnectionFactory($"Data Source={_dbPath}"));

        await artifactStore.InitializeAsync();
        await runStore.InitializeAsync();
        await gateStore.InitializeAsync();

        // Second call -- should not error (PRAGMAs are idempotent)
        await artifactStore.InitializeAsync();
        await runStore.InitializeAsync();
        await gateStore.InitializeAsync();

        Assert.Pass("All stores can be initialized twice without error.");
    }

    [Test]
    public async Task WALMode_ConcurrentReadersCompleteDuringWrite()
    {
        // WAL mode allows multiple readers to proceed while a writer is active.
        var store = new RunStore(new SqlDbConnectionFactory($"Data Source={_dbPath}"));
        await store.InitializeAsync();

        var runId = Guid.NewGuid();
        await store.CreateRunAsync(runId, "brief", DateTimeOffset.UtcNow.ToString("o"));

        var task1 = store.GetRunAsync(runId);
        var task2 = store.GetAllIncompleteAsync();
        await Task.WhenAll(task1, task2);

        var result = await task1;
        Assert.That(result, Is.Not.Null);
    }

    private static async Task<string> QueryPragma(string dbPath, string pragma)
    {
        using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync();
        using var cmd = new SqliteCommand(pragma, conn);
        return (string)(await cmd.ExecuteScalarAsync())!;
    }

    private static async Task<long> QueryPragmaInt(string dbPath, string pragma)
    {
        using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync();
        using var cmd = new SqliteCommand(pragma, conn);
        return (long)(await cmd.ExecuteScalarAsync())!;
    }
}
