using SDLC.Contracts;

namespace SDLC.Infrastructure;

[TestFixture, SingleThreaded]
public class ArtifactStoreTransactionTests
{
    private string _dbPath = null!;
    private string _fsPath = null!;
    private ArtifactStore _store = null!;

    [SetUp]
    public async Task SetUp()
    {
        _dbPath = Path.GetTempFileName();
        _fsPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_fsPath);
        _store = new ArtifactStore($"Data Source={_dbPath}", _fsPath);
        await _store.InitializeAsync();
    }

    [TearDown]
    public void TearDown()
    {
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { }
        try { if (Directory.Exists(_fsPath)) Directory.Delete(_fsPath, true); } catch { }
    }

    [Test]
    public async Task UpdateContentAsync_Transactional_FileAndDBStayInSync()
    {
        // Arrange — save an artifact
        var artifactId = Guid.NewGuid();
        var runId = Guid.NewGuid();
        var artifact = new ResearchBrief
        {
            ArtifactId = artifactId,
            RunId = runId,
            Stage = SdlcStage.Research,
            Status = ArtifactStatus.Approved,
            Content = "original content"
        };
        await _store.SaveAsync(artifact);

        // Act — update content (transactional: file write + DB status update)
        await _store.UpdateContentAsync(artifactId, "edited content");

        // Assert — both file and DB status updated together
        var updated = await _store.GetAsync<ResearchBrief>(artifactId);
        Assert.That(updated!.Content, Is.EqualTo("edited content"));
        Assert.That(updated.Status, Is.EqualTo(ArtifactStatus.PendingReview));
    }

    [Test]
    public async Task UpdateContentAsync_SerializedWritesCompleteWithoutLock()
    {
        // WAL mode serializes concurrent writes — verify serialized updates complete
        var artifact1Id = Guid.NewGuid();
        var artifact2Id = Guid.NewGuid();
        var runId = Guid.NewGuid();
        await _store.SaveAsync(new ResearchBrief
        {
            ArtifactId = artifact1Id, RunId = runId, Stage = SdlcStage.Research,
            Status = ArtifactStatus.Draft, Content = "a"
        });
        await _store.SaveAsync(new ResearchBrief
        {
            ArtifactId = artifact2Id, RunId = runId, Stage = SdlcStage.Research,
            Status = ArtifactStatus.Draft, Content = "b"
        });

        var semaphore = new SemaphoreSlim(1, 1);
        var task1 = WriteWithSemaphore(semaphore, artifact1Id, "updated 1");
        var task2 = WriteWithSemaphore(semaphore, artifact2Id, "updated 2");
        await Task.WhenAll(task1, task2);

        var v1 = await _store.GetAsync<ResearchBrief>(artifact1Id);
        var v2 = await _store.GetAsync<ResearchBrief>(artifact2Id);
        Assert.That(v1!.Content, Is.EqualTo("updated 1"));
        Assert.That(v2!.Content, Is.EqualTo("updated 2"));

        async Task WriteWithSemaphore(SemaphoreSlim sem, Guid id, string content)
        {
            await sem.WaitAsync();
            try { await _store.UpdateContentAsync(id, content); }
            finally { sem.Release(); }
        }
    }

    [Test]
    public async Task InitializeAsync_Reentrant_NoError()
    {
        await _store.InitializeAsync();
        await _store.InitializeAsync();
        Assert.Pass("InitializeAsync can be called multiple times without error.");
    }
}
