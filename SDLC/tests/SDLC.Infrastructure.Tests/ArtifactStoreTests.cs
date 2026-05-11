using FluentAssertions;
using SDLC.Contracts;
using NUnit.Framework;

namespace SDLC.Infrastructure.Tests;

[TestFixture, SingleThreaded]
public class ArtifactStoreTests
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
        _store = new ArtifactStore(new SqlDbConnectionFactory($"Data Source={_dbPath}"), _fsPath);
        await _store.InitializeAsync();
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            if (File.Exists(_dbPath))
                File.Delete(_dbPath);
            if (Directory.Exists(_fsPath))
                Directory.Delete(_fsPath, recursive: true);
        }
        catch { }
    }

    [Test]
    public async Task SaveAsync_ResearchBrief_PersistsContent()
    {
        var brief = new ResearchBrief
        {
            RunId = Guid.NewGuid(),
            Stage = SdlcStage.Research,
            Content = "# Research\nSome content here"
        };

        await _store.SaveAsync(brief);

        var retrieved = await _store.GetAsync<ResearchBrief>(brief.ArtifactId);
        retrieved.Should().NotBeNull();
        retrieved!.Content.Should().Be(brief.Content);
    }

    [Test]
    public async Task SaveAsync_WritesMarkdownFileToDisk()
    {
        var runId = Guid.NewGuid();
        var brief = new ResearchBrief
        {
            RunId = runId,
            Stage = SdlcStage.Research,
            Content = "# Research content"
        };

        await _store.SaveAsync(brief);

        var expectedPath = Path.Combine(_fsPath, runId.ToString(), $"Research-{brief.ArtifactId:N}.md");
        File.Exists(expectedPath).Should().BeTrue();
        (await File.ReadAllTextAsync(expectedPath)).Should().Be(brief.Content);
    }

    [Test]
    public async Task GetAsync_NonExistentArtifact_ReturnsNull()
    {
        var result = await _store.GetAsync<ResearchBrief>(Guid.NewGuid());
        result.Should().BeNull();
    }

    [Test]
    public async Task GetLatestForRunAsync_ReturnsCorrectStageArtifact()
    {
        var runId = Guid.NewGuid();
        var brief = new ResearchBrief { RunId = runId, Stage = SdlcStage.Research, Content = "brief" };
        var spec = new RequirementsSpec { RunId = runId, Stage = SdlcStage.Requirements, Content = "spec" };

        await _store.SaveAsync(brief);
        await _store.SaveAsync(spec);

        var retrieved = await _store.GetLatestForRunAsync<RequirementsSpec>(runId);
        retrieved.Should().NotBeNull();
        retrieved!.Content.Should().Be("spec");
    }

    [Test]
    public async Task UpdateStatusAsync_ChangesArtifactStatus()
    {
        var brief = new ResearchBrief
        {
            RunId = Guid.NewGuid(),
            Stage = SdlcStage.Research,
            Content = "content"
        };
        await _store.SaveAsync(brief);

        await _store.UpdateStatusAsync(brief.ArtifactId, ArtifactStatus.Approved);

        var retrieved = await _store.GetAsync<ResearchBrief>(brief.ArtifactId);
        retrieved!.Status.Should().Be(ArtifactStatus.Approved);
    }

    [Test]
    public async Task UpdateContentAsync_OverwritesFileAndMetadata()
    {
        var brief = new ResearchBrief
        {
            RunId = Guid.NewGuid(),
            Stage = SdlcStage.Research,
            Content = "original content"
        };
        await _store.SaveAsync(brief);

        await _store.UpdateContentAsync(brief.ArtifactId, "updated content");

        var retrieved = await _store.GetAsync<ResearchBrief>(brief.ArtifactId);
        retrieved!.Content.Should().Be("updated content");
    }

    [Test]
    public async Task GetAllForRunAsync_ReturnsArtifactsInStageOrder()
    {
        var runId = Guid.NewGuid();
        await _store.SaveAsync(new RequirementsSpec { RunId = runId, Stage = SdlcStage.Requirements });
        await _store.SaveAsync(new ResearchBrief { RunId = runId, Stage = SdlcStage.Research });

        var all = await _store.GetAllForRunAsync(runId);

        all.Should().HaveCount(2);
        all[0].Stage.Should().Be(SdlcStage.Research);
        all[1].Stage.Should().Be(SdlcStage.Requirements);
    }
}
