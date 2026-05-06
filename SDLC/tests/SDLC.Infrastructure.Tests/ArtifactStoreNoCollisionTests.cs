using FluentAssertions;
using SDLC.Contracts;
using NUnit.Framework;

namespace SDLC.Infrastructure.Tests;

/// <summary>
/// ArtifactStore no file path collision on re-run: same type, same run produces distinct files.
/// </summary>
[TestFixture, SingleThreaded]
public class ArtifactStoreNoCollisionTests
{
    private ArtifactStore _store = null!;
    private string _dbPath = null!;
    private string _fsPath = null!;

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
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); if (Directory.Exists(_fsPath)) Directory.Delete(_fsPath, true); } catch { }
    }

    [Test]
    public async Task SaveAsync_TwoResearchBriefsSameRun_TwoDistinctFilesOnDisk()
    {
        var runId = Guid.NewGuid();
        var brief1 = new ResearchBrief { RunId = runId, Stage = SdlcStage.Research, Content = "first", CreatedAt = DateTimeOffset.UtcNow.AddHours(-1) };
        var brief2 = new ResearchBrief { RunId = runId, Stage = SdlcStage.Research, Content = "second", CreatedAt = DateTimeOffset.UtcNow };

        await _store.SaveAsync(brief1);
        await _store.SaveAsync(brief2);

        File.Exists(Path.Combine(_fsPath, runId.ToString(), $"Research-{brief1.ArtifactId:N}.md")).Should().BeTrue();
        File.Exists(Path.Combine(_fsPath, runId.ToString(), $"Research-{brief2.ArtifactId:N}.md")).Should().BeTrue();
    }

    [Test]
    public async Task SaveAsync_TwoResearchBriefsSameRun_BothRetrievableById()
    {
        var runId = Guid.NewGuid();
        var brief1 = new ResearchBrief { RunId = runId, Stage = SdlcStage.Research, Content = "first" };
        var brief2 = new ResearchBrief { RunId = runId, Stage = SdlcStage.Research, Content = "second" };

        await _store.SaveAsync(brief1);
        await _store.SaveAsync(brief2);

        var retrieved1 = await _store.GetAsync<ResearchBrief>(brief1.ArtifactId);
        var retrieved2 = await _store.GetAsync<ResearchBrief>(brief2.ArtifactId);

        retrieved1!.Content.Should().Be("first");
        retrieved2!.Content.Should().Be("second");
    }
}
