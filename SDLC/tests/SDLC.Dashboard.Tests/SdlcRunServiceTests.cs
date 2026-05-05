using System.Collections.Concurrent;
using FluentAssertions;
using NUnit.Framework;
using SDLC.Contracts;
using SDLC.Dashboard.Services;
using SDLC.Infrastructure;
using SDLC.Orchestrator;

namespace SDLC.Dashboard.Tests;

[TestFixture, SingleThreaded]
public class SdlcRunServiceTests
{
    private TestArtifactStore _artifactStore = null!;
    private TestGateStore _gateStore = null!;
    private TestRunner _runner = null!;
    private Guid _testRunId;

    [SetUp]
    public void SetUp()
    {
        _testRunId = Guid.NewGuid();
        _artifactStore = new TestArtifactStore();
        _gateStore = new TestGateStore();
        _runner = new TestRunner();
    }

    [Test]
    public void Constructor_AssemblesDependencies()
    {
        var service = new SdlcRunService(_artifactStore, _gateStore, _runner);
        service.Should().NotBeNull();
    }

    [Test]
    public async Task GetRunDetailAsync_ReturnsNull_WhenNoArtifacts()
    {
        var service = new SdlcRunService(_artifactStore, _gateStore, _runner);
        var result = await service.GetRunDetailAsync(_testRunId);
        result.Should().BeNull();
    }

    [Test]
    public async Task GetRunDetailAsync_ReturnsSummary_WhenArtifactsExist()
    {
        var brief = new ResearchBrief
        {
            ArtifactId = Guid.NewGuid(),
            Content = "research",
            RunId = _testRunId,
            Stage = SdlcStage.Research,
            Status = ArtifactStatus.Approved,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _artifactStore.Add(brief);

        var service = new SdlcRunService(_artifactStore, _gateStore, _runner);
        var result = await service.GetRunDetailAsync(_testRunId);

        result.Should().NotBeNull();
        result!.RunId.Should().Be(_testRunId);
        result.LastStage.Should().Be(SdlcStage.Research);
        result.Artifacts.Should().ContainSingle();
        result.Artifacts[0].TypeName.Should().Be("ResearchBrief");
    }

    [Test]
    public async Task GetRunDetailAsync_IncludesArtifactsFromRun()
    {
        var arch = new ArchitectureRecord
        {
            ArtifactId = Guid.NewGuid(),
            Content = "arch",
            RunId = _testRunId,
            Stage = SdlcStage.Design,
            Status = ArtifactStatus.PendingReview,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _artifactStore.Add(arch);

        var service = new SdlcRunService(_artifactStore, _gateStore, _runner);
        var result = await service.GetRunDetailAsync(_testRunId);

        result!.Artifacts.Should().ContainSingle();
        result.Artifacts[0].Stage.Should().Be(SdlcStage.Design);
        result.Artifacts[0].Status.Should().Be(ArtifactStatus.PendingReview);
    }

    [Test]
    public async Task GetRunDetailAsync_ActiveRunsReported()
    {
        var brief = new ResearchBrief
        {
            ArtifactId = Guid.NewGuid(),
            Content = "research",
            RunId = _testRunId,
            Stage = SdlcStage.Research,
            Status = ArtifactStatus.Approved,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _artifactStore.Add(brief);
        _runner.SetActive(_testRunId, true);

        var service = new SdlcRunService(_artifactStore, _gateStore, _runner);
        var result = await service.GetRunDetailAsync(_testRunId);

        result!.IsActive.Should().BeTrue();
    }

    [Test]
    public async Task GetRunDetailAsync_LastStageIsLatest()
    {
        var brief = new ResearchBrief
        {
            ArtifactId = Guid.NewGuid(),
            Content = "research",
            RunId = _testRunId,
            Stage = SdlcStage.Research,
            Status = ArtifactStatus.Approved,
            CreatedAt = DateTimeOffset.UtcNow.AddHours(-1)
        };
        var arch = new ArchitectureRecord
        {
            ArtifactId = Guid.NewGuid(),
            Content = "arch",
            RunId = _testRunId,
            Stage = SdlcStage.Design,
            Status = ArtifactStatus.Approved,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _artifactStore.Add(brief);
        _artifactStore.Add(arch);

        var service = new SdlcRunService(_artifactStore, _gateStore, _runner);
        var result = await service.GetRunDetailAsync(_testRunId);

        result!.LastStage.Should().Be(SdlcStage.Design);
        result.Artifacts.Should().HaveCount(2);
    }

    [Test]
    public async Task ApproveGateAsync_CallsGateStoreResolve()
    {
        var gateId = Guid.NewGuid();
        var gate = new StageGate
        {
            GateId = gateId,
            RunId = _testRunId,
            Stage = SdlcStage.Requirements,
            Status = GateStatus.Pending
        };
        _gateStore.Gates[gateId] = gate;

        var service = new SdlcRunService(_artifactStore, _gateStore, _runner);
        await service.ApproveGateAsync(gateId);

        gate.Status.Should().Be(GateStatus.Approved);
        gate.Notes.Should().BeNull();
        _runner.ResumedGate.Should().Be(gateId);
    }

    [Test]
    public async Task ApproveGateAsync_Throws_WhenGateNotFound()
    {
        var gateId = Guid.NewGuid();

        var service = new SdlcRunService(_artifactStore, _gateStore, _runner);

        var act = async () => await service.ApproveGateAsync(gateId);
        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Test]
    public async Task RejectGateAsync_CallsGateStoreResolveWithRejected()
    {
        var gateId = Guid.NewGuid();
        var notes = "Not ready yet";
        var gate = new StageGate
        {
            GateId = gateId,
            RunId = _testRunId,
            Stage = SdlcStage.Design,
            Status = GateStatus.Pending
        };
        _gateStore.Gates[gateId] = gate;

        var service = new SdlcRunService(_artifactStore, _gateStore, _runner);
        await service.RejectGateAsync(gateId, notes);

        gate.Status.Should().Be(GateStatus.Rejected);
        gate.Notes.Should().Be(notes);
    }

    private class TestArtifactStore : IArtifactStore
    {
        private readonly List<SdlcArtifact> _artifacts = new();
        private readonly ConcurrentDictionary<Guid, List<SdlcArtifact>> _byRun = new();

        public void Add(SdlcArtifact artifact)
        {
            _artifacts.Add(artifact);
            _byRun.AddOrUpdate(artifact.RunId, new List<SdlcArtifact> { artifact }, (_, existing) =>
            {
                existing.Add(artifact);
                return existing;
            });
        }

        public Task SaveAsync(SdlcArtifact artifact)
        {
            Add(artifact);
            return Task.CompletedTask;
        }

        public Task<T?> GetAsync<T>(Guid artifactId) where T : SdlcArtifact =>
            Task.FromResult(_artifacts.OfType<T>().FirstOrDefault(a => a.ArtifactId == artifactId));

        public Task<T?> GetLatestForRunAsync<T>(Guid runId) where T : SdlcArtifact =>
            Task.FromResult(_artifacts.Where(a => a.RunId == runId).OfType<T>().MaxBy(a => a.CreatedAt));

        public Task UpdateStatusAsync(Guid artifactId, ArtifactStatus status) => throw new NotImplementedException();
        public Task UpdateContentAsync(Guid artifactId, string content) => throw new NotImplementedException();
        public Task<List<SdlcArtifact>> GetAllForRunAsync(Guid runId) =>
            Task.FromResult(_byRun.TryGetValue(runId, out var list) ? list : new List<SdlcArtifact>());
    }

    private class TestGateStore : IStageGateStore
    {
        public ConcurrentDictionary<Guid, StageGate> Gates { get; } = new();

        public Task<StageGate> CreateGateAsync(SdlcArtifact artifact)
        {
            var gate = new StageGate { RunId = artifact.RunId, Stage = artifact.Stage, Artifact = artifact };
            Gates[gate.GateId] = gate;
            return Task.FromResult(gate);
        }

        public Task<StageGate?> GetAsync(Guid gateId) => Task.FromResult(Gates.GetValueOrDefault(gateId));

        public Task ResolveAsync(Guid gateId, GateDecision decision, string? notes)
        {
            if (Gates.TryGetValue(gateId, out var gate))
            {
                gate.Status = decision == GateDecision.Approved ? GateStatus.Approved : GateStatus.Rejected;
                gate.Notes = notes;
                gate.ResolvedAt = DateTimeOffset.UtcNow;
            }
            return Task.CompletedTask;
        }

        public Task<List<StageGate>> GetPendingForRunAsync(Guid runId) =>
            Task.FromResult(Gates.Values.Where(g => g.RunId == runId && g.Status == GateStatus.Pending).ToList());
    }

    private class TestRunner : PipelineRunnerService
    {
        private readonly Dictionary<Guid, bool> _activeRuns = new();
        public Guid ResumedGate { get; private set; }

        public TestRunner() : base(null!, null!) { }

        public void SetActive(Guid runId, bool active)
        {
            if (active)
                _activeRuns[runId] = true;
            else
                _activeRuns.Remove(runId);
        }

        public override bool IsRunActive(Guid runId) => _activeRuns.ContainsKey(runId);

        public override async Task ResumeGateAsync(Guid runId, Guid gateId, GateDecision decision, string? notes, CancellationToken ct = default)
        {
            ResumedGate = gateId;
        }
    }
}
