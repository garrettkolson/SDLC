using FluentAssertions;
using NUnit.Framework;
using SDLC.Contracts;
using SDLC.Infrastructure;

namespace SDLC.Integration.Tests;

/// <summary>
/// Full pipeline with real SQLite + file store.
/// Tests artifact save → retrieve → status update → gate create → resolve → retrieve flow.
/// </summary>
[TestFixture]
public class ArtifactAndGatePipelineTests
{
    private string _dbPath = null!;
    private string _tempDir = null!;
    private ArtifactStore _artifactStore = null!;
    private StageGateStore _gateStore = null!;

    [SetUp]
    public async Task SetUp()
    {
        _dbPath = Path.GetTempFileName();
        _tempDir = Path.Combine(Path.GetTempPath(), $"sdlc-integration-{Guid.NewGuid():N}");
        _artifactStore = new ArtifactStore($"Data Source={_dbPath}", _tempDir);
        _gateStore = new StageGateStore($"Data Source={_dbPath}");
        await _artifactStore.InitializeAsync();
        await _gateStore.InitializeAsync();
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            File.Delete(_dbPath);
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, true);
        }
        catch { /* best-effort cleanup */ }
    }

    [Test]
    public async Task ArtifactLifecycle_SaveAndRetrieve_ResearchBrief()
    {
        var brief = new ResearchBrief
        {
            ArtifactId = Guid.NewGuid(),
            Content = "Research findings about the project domain",
            RunId = Guid.NewGuid(),
            Stage = SdlcStage.Research,
            Status = ArtifactStatus.Draft,
            CreatedAt = DateTimeOffset.UtcNow
        };

        await _artifactStore.SaveAsync(brief);

        var retrieved = await _artifactStore.GetAsync<ResearchBrief>(brief.ArtifactId);

        retrieved.Should().NotBeNull();
        retrieved!.Content.Should().Be(brief.Content);
        retrieved.RunId.Should().Be(brief.RunId);
        retrieved.Stage.Should().Be(SdlcStage.Research);
    }

    [Test]
    public async Task ArtifactLifecycle_RequirementsToDesignPipeline()
    {
        var runId = Guid.NewGuid();
        var req = new RequirementsSpec
        {
            ArtifactId = Guid.NewGuid(),
            Content = "User must be able to login",
            RunId = runId,
            Stage = SdlcStage.Requirements,
            Status = ArtifactStatus.Approved,
            CreatedAt = DateTimeOffset.UtcNow.AddHours(-1)
        };
        var arch = new ArchitectureRecord
        {
            ArtifactId = Guid.NewGuid(),
            Content = "System uses JWT auth",
            RunId = runId,
            Stage = SdlcStage.Design,
            Status = ArtifactStatus.Approved,
            CreatedAt = DateTimeOffset.UtcNow
        };

        await _artifactStore.SaveAsync(req);
        await _artifactStore.SaveAsync(arch);

        var all = await _artifactStore.GetAllForRunAsync(runId);

        all.Should().HaveCount(2);
        all[0].Stage.Should().Be(SdlcStage.Requirements);
        all[1].Stage.Should().Be(SdlcStage.Design);
    }

    [Test]
    public async Task ArtifactLifecycle_UpdateStatusChangesState()
    {
        var artifactId = Guid.NewGuid();
        var brief = new ResearchBrief
        {
            ArtifactId = artifactId,
            Content = "research",
            RunId = Guid.NewGuid(),
            Stage = SdlcStage.Research,
            Status = ArtifactStatus.Draft,
            CreatedAt = DateTimeOffset.UtcNow
        };

        await _artifactStore.SaveAsync(brief);
        await _artifactStore.UpdateStatusAsync(artifactId, ArtifactStatus.PendingReview);

        var updated = await _artifactStore.GetAsync<ResearchBrief>(artifactId);
        updated!.Status.Should().Be(ArtifactStatus.PendingReview);
    }

    [Test]
    public async Task ArtifactLifecycle_UpdateContentUpdatesFile()
    {
        var artifactId = Guid.NewGuid();
        var brief = new ResearchBrief
        {
            ArtifactId = artifactId,
            Content = "initial",
            RunId = Guid.NewGuid(),
            Stage = SdlcStage.Research,
            Status = ArtifactStatus.Draft,
            CreatedAt = DateTimeOffset.UtcNow
        };

        await _artifactStore.SaveAsync(brief);
        await _artifactStore.UpdateContentAsync(artifactId, "updated content");

        var updated = await _artifactStore.GetAsync<ResearchBrief>(artifactId);
        updated!.Content.Should().Be("updated content");
        updated.Status.Should().Be(ArtifactStatus.PendingReview);
    }

    [Test]
    public async Task GateLifecycle_CreateGate_StoresArtifactReference()
    {
        var runId = Guid.NewGuid();
        var brief = new ResearchBrief
        {
            ArtifactId = Guid.NewGuid(),
            Content = "research",
            RunId = runId,
            Stage = SdlcStage.Research,
            Status = ArtifactStatus.Approved,
            CreatedAt = DateTimeOffset.UtcNow
        };
        await _artifactStore.SaveAsync(brief);

        var gate = await _gateStore.CreateGateAsync(brief);

        gate.Should().NotBeNull();
        gate!.RunId.Should().Be(runId);
        gate.Stage.Should().Be(SdlcStage.Research);
        gate.Status.Should().Be(GateStatus.Pending);
    }

    [Test]
    public async Task GateLifecycle_ResolveAsApproved()
    {
        var runId = Guid.NewGuid();
        var brief = new ResearchBrief
        {
            ArtifactId = Guid.NewGuid(),
            Content = "research",
            RunId = runId,
            Stage = SdlcStage.Research,
            Status = ArtifactStatus.Approved,
            CreatedAt = DateTimeOffset.UtcNow
        };
        await _artifactStore.SaveAsync(brief);
        var gate = await _gateStore.CreateGateAsync(brief);

        await _gateStore.ResolveAsync(gate.GateId, GateDecision.Approved, "Looks good", "integration-user", "Integration User");

        var resolved = await _gateStore.GetAsync(gate.GateId);

        resolved!.Status.Should().Be(GateStatus.Approved);
        resolved.Notes.Should().Be("Looks good");
        resolved.ResolvedAt.Should().NotBeNull();
    }

    [Test]
    public async Task GateLifecycle_ResolveAsRejected()
    {
        var runId = Guid.NewGuid();
        var brief = new ResearchBrief
        {
            ArtifactId = Guid.NewGuid(),
            Content = "research",
            RunId = runId,
            Stage = SdlcStage.Research,
            Status = ArtifactStatus.Approved,
            CreatedAt = DateTimeOffset.UtcNow
        };
        await _artifactStore.SaveAsync(brief);
        var gate = await _gateStore.CreateGateAsync(brief);

        await _gateStore.ResolveAsync(gate.GateId, GateDecision.Rejected, "Needs more work", "integration-user", "Integration User");

        var resolved = await _gateStore.GetAsync(gate.GateId);

        resolved!.Status.Should().Be(GateStatus.Rejected);
    }

    [Test]
    public async Task GateLifecycle_GetPendingForRun_ExcludesResolved()
    {
        var runId = Guid.NewGuid();
        var brief = new ResearchBrief
        {
            ArtifactId = Guid.NewGuid(),
            Content = "research",
            RunId = runId,
            Stage = SdlcStage.Research,
            Status = ArtifactStatus.Approved,
            CreatedAt = DateTimeOffset.UtcNow
        };
        await _artifactStore.SaveAsync(brief);
        var gate = await _gateStore.CreateGateAsync(brief);

        await _gateStore.ResolveAsync(gate.GateId, GateDecision.Approved, null, "integration-user", "Integration User");

        var pending = await _gateStore.GetPendingForRunAsync(runId);
        pending.Should().BeEmpty();
    }

    [Test]
    public async Task GateLifecycle_GetPendingForRun_OnlyReturnsPending()
    {
        var runId = Guid.NewGuid();
        var brief = new ResearchBrief
        {
            ArtifactId = Guid.NewGuid(),
            Content = "research",
            RunId = runId,
            Stage = SdlcStage.Research,
            Status = ArtifactStatus.Approved,
            CreatedAt = DateTimeOffset.UtcNow
        };
        await _artifactStore.SaveAsync(brief);
        var pendingGate = await _gateStore.CreateGateAsync(brief);

        var resolvedBrief = new ResearchBrief
        {
            ArtifactId = Guid.NewGuid(),
            Content = "arch",
            RunId = runId,
            Stage = SdlcStage.Design,
            Status = ArtifactStatus.Approved,
            CreatedAt = DateTimeOffset.UtcNow.AddHours(1)
        };
        await _artifactStore.SaveAsync(resolvedBrief);
        var resolvedGate = await _gateStore.CreateGateAsync(resolvedBrief);
        await _gateStore.ResolveAsync(resolvedGate.GateId, GateDecision.Approved, null, "integration-user", "Integration User");

        var pending = await _gateStore.GetPendingForRunAsync(runId);
        pending.Should().ContainSingle(g => g.GateId == pendingGate.GateId);
    }

    [Test]
    public async Task EndToEnd_GetLatestForRunAsync_Pipeline()
    {
        var runId = Guid.NewGuid();
        var brief = new ResearchBrief
        {
            ArtifactId = Guid.NewGuid(),
            Content = "research",
            RunId = runId,
            Stage = SdlcStage.Research,
            Status = ArtifactStatus.Approved,
            CreatedAt = DateTimeOffset.UtcNow.AddHours(-2)
        };
        var req = new RequirementsSpec
        {
            ArtifactId = Guid.NewGuid(),
            Content = "requirements",
            RunId = runId,
            Stage = SdlcStage.Requirements,
            Status = ArtifactStatus.Approved,
            CreatedAt = DateTimeOffset.UtcNow.AddHours(-1)
        };
        var arch = new ArchitectureRecord
        {
            ArtifactId = Guid.NewGuid(),
            Content = "architecture",
            RunId = runId,
            Stage = SdlcStage.Design,
            Status = ArtifactStatus.Approved,
            CreatedAt = DateTimeOffset.UtcNow
        };

        await _artifactStore.SaveAsync(brief);
        await _artifactStore.SaveAsync(req);
        await _artifactStore.SaveAsync(arch);

        var latestResearch = await _artifactStore.GetLatestForRunAsync<ResearchBrief>(runId);
        var latestReq = await _artifactStore.GetLatestForRunAsync<RequirementsSpec>(runId);
        var latestArch = await _artifactStore.GetLatestForRunAsync<ArchitectureRecord>(runId);

        latestResearch.Should().NotBeNull();
        latestResearch!.Content.Should().Be("research");
        latestReq.Should().NotBeNull();
        latestReq!.Content.Should().Be("requirements");
        latestArch.Should().NotBeNull();
        latestArch!.Content.Should().Be("architecture");
    }

    [Test]
    public async Task EndToEnd_MultipleArtifactsSameType_AllRetrieved()
    {
        var runId = Guid.NewGuid();
        for (var i = 0; i < 3; i++)
        {
            var brief = new ResearchBrief
            {
                ArtifactId = Guid.NewGuid(),
                Content = $"research-{i}",
                RunId = runId,
                Stage = SdlcStage.Research,
                Status = ArtifactStatus.Draft,
                CreatedAt = DateTimeOffset.UtcNow.AddHours(i)
            };
            await _artifactStore.SaveAsync(brief);
        }

        var all = await _artifactStore.GetAllForRunAsync(runId);
        all.Should().HaveCount(3);
    }
}
