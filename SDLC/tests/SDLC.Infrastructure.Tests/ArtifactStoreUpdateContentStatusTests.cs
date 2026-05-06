using FluentAssertions;
using SDLC.Contracts;
using NUnit.Framework;

namespace SDLC.Infrastructure.Tests;

/// <summary>
/// UpdateContentAsync resets to PendingReview (not Draft) requiring re-approval.
/// </summary>
[TestFixture, SingleThreaded]
public class ArtifactStoreUpdateContentStatusTests
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
    public async Task UpdateContentAsync_OnApprovedArtifact_StatusBecomesPendingReviewNotDraft()
    {
        var brief = new ResearchBrief
        {
            RunId = Guid.NewGuid(),
            Stage = SdlcStage.Research,
            Content = "original",
            Status = ArtifactStatus.Approved
        };
        await _store.SaveAsync(brief);

        await _store.UpdateContentAsync(brief.ArtifactId, "edited content");

        var updated = await _store.GetAsync<ResearchBrief>(brief.ArtifactId);
        updated!.Status.Should().Be(ArtifactStatus.PendingReview);
        updated.Status.Should().NotBe(ArtifactStatus.Draft);
    }
}
