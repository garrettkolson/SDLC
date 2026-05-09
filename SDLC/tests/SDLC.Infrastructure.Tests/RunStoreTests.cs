using FluentAssertions;
using NUnit.Framework;
using SDLC.Contracts;
using SDLC.Infrastructure;

namespace SDLC.Infrastructure.Tests;

[TestFixture, SingleThreaded]
public class RunStoreTests
{
    private string _tempFile = null!;
    private RunStore _store = null!;

    [SetUp]
    public async Task SetUp()
    {
        _tempFile = Path.Combine(Path.GetTempPath(), $"test_runs_{Guid.NewGuid()}.db");
        var connString = $"Data Source={_tempFile}";
        _store = new RunStore(connString);
        await _store.InitializeAsync();
    }

    [TearDown]
    public void Cleanup()
    {
        try { File.Delete(_tempFile); } catch { /* ignore */ }
    }

    private RunStore GetStore() => new RunStore($"Data Source={_tempFile}");

    [Test]
    public async Task CreateRunAsync_StoresRun()
    {
        var runId = Guid.NewGuid();
        await GetStore().CreateRunAsync(runId, "Test brief", DateTimeOffset.UtcNow.ToString("o"));

        var checkpoint = await GetStore().GetRunAsync(runId);
        checkpoint.Should().NotBeNull();
        checkpoint!.RunId.Should().Be(runId);
        checkpoint.CurrentStage.Should().Be("Research");
        checkpoint.Status.Should().Be("Running");
    }

    [Test]
    public async Task UpdateStageAsync_UpdatesStageAndStatus()
    {
        var runId = Guid.NewGuid();
        await GetStore().CreateRunAsync(runId, "Test", DateTimeOffset.UtcNow.ToString("o"));

        await GetStore().UpdateStageAsync(runId, "Design", "Running");

        var checkpoint = await GetStore().GetRunAsync(runId);
        checkpoint!.CurrentStage.Should().Be("Design");
        checkpoint.Status.Should().Be("Running");
    }

    [Test]
    public async Task GetAllIncompleteAsync_ReturnsRunningAndFailed()
    {
        var runId1 = Guid.NewGuid();
        var runId2 = Guid.NewGuid();
        var runId3 = Guid.NewGuid();

        await GetStore().CreateRunAsync(runId1, "A", DateTimeOffset.UtcNow.ToString("o"));
        await GetStore().CreateRunAsync(runId2, "B", DateTimeOffset.UtcNow.ToString("o"));
        await GetStore().CreateRunAsync(runId3, "C", DateTimeOffset.UtcNow.ToString("o"));

        await GetStore().UpdateStageAsync(runId1, "Completed", "Completed");
        await GetStore().UpdateStageAsync(runId2, "Design", "Running");
        await GetStore().UpdateStageAsync(runId3, "Build", "Failed");

        var results = await GetStore().GetAllIncompleteAsync();

        results.Should().HaveCount(2);
        results.Should().Contain(r => r.RunId == runId2 && r.Status == "Running");
        results.Should().Contain(r => r.RunId == runId3 && r.Status == "Failed");
    }

    [Test]
    public async Task GetAllIncompleteAsync_ExcludesCompleted()
    {
        var runId = Guid.NewGuid();
        await GetStore().CreateRunAsync(runId, "A", DateTimeOffset.UtcNow.ToString("o"));
        await GetStore().UpdateStageAsync(runId, "Learn", "Completed");

        var results = await GetStore().GetAllIncompleteAsync();

        results.Should().BeEmpty();
    }

    [Test]
    public async Task GetRunAsync_ReturnsNull_ForNonExistentRun()
    {
        var result = await GetStore().GetRunAsync(Guid.NewGuid());

        result.Should().BeNull();
    }
}
