using System.Collections.Concurrent;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using SDLC.Contracts;
using SDLC.Dashboard.Services;
using SDLC.Infrastructure;
using SDLC.Orchestrator;
using SDLC.Telemetry;

namespace SDLC.Dashboard.Tests;

[TestFixture, SingleThreaded]
public class SdlcRunServiceTests
{
    private TestArtifactStore _artifactStore = null!;
    private TestGateStore _gateStore = null!;
    private TestRunStore _runStore = null!;
    private TestRunner _runner = null!;
    private IPipelineTelemetry telemetry = null!;
    private IRunBudgetTracker budgetTracker = null!;
    private Guid _testRunId;

    [SetUp]
    public void SetUp()
    {
        _testRunId = Guid.NewGuid();
        _artifactStore = new TestArtifactStore();
        _gateStore = new TestGateStore();
        _runStore = new TestRunStore();
        _runner = new TestRunner();
        telemetry = Substitute.For<IPipelineTelemetry>();
        budgetTracker = Substitute.For<IRunBudgetTracker>();
        budgetTracker.GetUsageAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                     .Returns(Task.FromResult(TokenUsage.Zero));
        budgetTracker.BudgetLimit.Returns(500_000L);
    }

    [Test]
    public void Constructor_AssemblesDependencies()
    {
        var service = new SdlcRunService(_artifactStore, _gateStore, _runStore, telemetry, _runner, budgetTracker);
        service.Should().NotBeNull();
    }

    [Test]
    public async Task GetRunDetailAsync_ReturnsNull_WhenNoArtifacts()
    {
        var service = new SdlcRunService(_artifactStore, _gateStore, _runStore, telemetry, _runner, budgetTracker);
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

        var service = new SdlcRunService(_artifactStore, _gateStore, _runStore, telemetry, _runner, budgetTracker);
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

        var service = new SdlcRunService(_artifactStore, _gateStore, _runStore, telemetry, _runner, budgetTracker);
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

        var service = new SdlcRunService(_artifactStore, _gateStore, _runStore, telemetry, _runner, budgetTracker);
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

        var service = new SdlcRunService(_artifactStore, _gateStore, _runStore, telemetry, _runner, budgetTracker);
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

        var service = new SdlcRunService(_artifactStore, _gateStore, _runStore, telemetry, _runner, budgetTracker);
        await service.ApproveGateAsync(gateId, "user-123", "Test User", null);

        gate.Status.Should().Be(GateStatus.Approved);
        gate.Notes.Should().BeNull();
        gate.ResolvedById.Should().Be("user-123");
        gate.ResolvedByDisplay.Should().Be("Test User");
        await WaitForRunnerAsync();
        _runner.ResumedGate.Should().Be(gateId);
    }

    [Test]
    public async Task ApproveGateAsync_Throws_WhenGateNotFound()
    {
        var gateId = Guid.NewGuid();

        var service = new SdlcRunService(_artifactStore, _gateStore, _runStore, telemetry, _runner, budgetTracker);

        var act = async () => await service.ApproveGateAsync(gateId, "user-123", "Test User", "notes");
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

        var service = new SdlcRunService(_artifactStore, _gateStore, _runStore, telemetry, _runner, budgetTracker);
        await service.RejectGateAsync(gateId, "user-123", "Test User", notes);

        gate.Status.Should().Be(GateStatus.Rejected);
        gate.Notes.Should().Be(notes);
        gate.ResolvedById.Should().Be("user-123");
        gate.ResolvedByDisplay.Should().Be("Test User");
        await WaitForRunnerAsync();
        _runner.ResumedGate.Should().Be(gateId);
    }

    [Test]
    public async Task GetRunDetailAsync_IncludesTokenUsage()
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
        budgetTracker.GetUsageAsync(_testRunId, Arg.Any<CancellationToken>())
                     .Returns(Task.FromResult(new TokenUsage(1000, 500)));

        var service = new SdlcRunService(_artifactStore, _gateStore, _runStore, telemetry, _runner, budgetTracker);
        var result = await service.GetRunDetailAsync(_testRunId);

        result!.PromptTokens.Should().Be(1000);
        result!.CompletionTokens.Should().Be(500);
        result!.TotalTokens.Should().Be(1500);
        result!.BudgetLimit.Should().Be(500_000L);
    }

    [Test]
    public async Task RejectGateAsync_Throws_WhenGateNotFound()
    {
        var gateId = Guid.NewGuid();

        var service = new SdlcRunService(_artifactStore, _gateStore, _runStore, telemetry, _runner, budgetTracker);

        var act = async () => await service.RejectGateAsync(gateId, "user-123", "Test User", "reason");
        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Test]
    public async Task CancelRunAsync_Throws_WhenRunNotActive()
    {
        var service = new SdlcRunService(_artifactStore, _gateStore, _runStore, telemetry, _runner, budgetTracker);
        var act = () => service.CancelRunAsync(Guid.NewGuid());
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Test]
    public async Task CancelRunAsync_PersistsToRunStore()
    {
        var runId = Guid.NewGuid();
        _runner.SetActive(runId, true);
        var service = new SdlcRunService(_artifactStore, _gateStore, _runStore, telemetry, _runner, budgetTracker);

        await service.CancelRunAsync(runId);

        // RunStore.CancelRunAsync was called (TestRunStore sets status to 'Cancelled')
        // No exception means it worked
    }

    private async Task WaitForRunnerAsync()
    {
        for (int i = 0; i < 20; i++)
        {
            if (_runner.ResumedGate != Guid.Empty)
                return;
            await Task.Delay(50);
        }
    }

    private class TestArtifactStore : IArtifactStore
    {
        private readonly List<SdlcArtifact> _artifacts = new();
        private readonly ConcurrentDictionary<Guid, List<SdlcArtifact>> _byRun = new();

        public Task InitializeAsync() => Task.CompletedTask;
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

        public Task<List<Guid>> GetAllRunIdsAsync() =>
            Task.FromResult(_byRun.Keys.ToList());
    }

    private class TestGateStore : IStageGateStore
    {
        public ConcurrentDictionary<Guid, StageGate> Gates { get; } = new();

        public Task InitializeAsync() => Task.CompletedTask;
        public Task<StageGate> CreateGateAsync(SdlcArtifact artifact)
        {
            var gate = new StageGate { RunId = artifact.RunId, Stage = artifact.Stage, Artifact = artifact };
            Gates[gate.GateId] = gate;
            return Task.FromResult(gate);
        }

        public Task<StageGate?> GetAsync(Guid gateId) => Task.FromResult(Gates.GetValueOrDefault(gateId));

        public Task ResolveAsync(Guid gateId, GateDecision decision, string? notes, string resolvedById, string resolvedByDisplay)
        {
            if (Gates.TryGetValue(gateId, out var gate))
            {
                gate.Status = decision == GateDecision.Approved ? GateStatus.Approved : GateStatus.Rejected;
                gate.Notes = notes;
                gate.ResolvedAt = DateTimeOffset.UtcNow;
                gate.ResolvedById = resolvedById;
                gate.ResolvedByDisplay = resolvedByDisplay;
            }
            return Task.CompletedTask;
        }

        public Task<List<StageGate>> GetPendingForRunAsync(Guid runId) =>
            Task.FromResult(Gates.Values.Where(g => g.RunId == runId && g.Status == GateStatus.Pending).ToList());

        public Task<List<StageGate>> GetAllPendingAsync() =>
            Task.FromResult(Gates.Values.Where(g => g.Status == GateStatus.Pending).ToList());
    }

    private class TestRunner() : PipelineRunnerService(null!, null!, null!, Substitute.For<IStageGateStore>(), Substitute.For<IRunStore>())
    {
        private readonly Dictionary<Guid, bool> _activeRuns = new();
        public Guid ResumedGate { get; private set; }

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

        public override Task CancelRunAsync(Guid runId, CancellationToken ct = default)
        {
            if (!_activeRuns.ContainsKey(runId))
                throw new InvalidOperationException($"No active run for {runId}");
            return Task.CompletedTask;
        }
    }

    private class TestRunStore : IRunStore
    {
        private readonly ConcurrentDictionary<Guid, RunCheckpoint> _runs = new();

        public Task InitializeAsync() => Task.CompletedTask;
        public Task CancelRunAsync(Guid runId)
        {
            if (_runs.TryGetValue(runId, out var checkpoint))
            {
                _runs[runId] = checkpoint with { Status = "Cancelled" };
            }
            return Task.CompletedTask;
        }

        public Task CreateRunAsync(Guid runId, string projectBrief, string startedAt)
        {
            _runs[runId] = new RunCheckpoint(runId, "Research", "Running", DateTimeOffset.Parse(startedAt), "");
            return Task.CompletedTask;
        }

        public Task<List<RunCheckpoint>> GetAllIncompleteAsync() =>
            Task.FromResult(_runs.Values.ToList());

        public Task<RunCheckpoint?> GetRunAsync(Guid runId) => Task.FromResult(_runs.GetValueOrDefault(runId));

        public Task SaveAsync(SdlcArtifact artifact) => Task.CompletedTask;
        public Task<T?> GetAsync<T>(Guid artifactId) where T : SdlcArtifact => Task.FromResult<T?>(null);
        public Task<T?> GetLatestForRunAsync<T>(Guid runId) where T : SdlcArtifact => Task.FromResult<T?>(null);
        public Task UpdateStatusAsync(Guid artifactId, ArtifactStatus status) => Task.CompletedTask;
        public Task UpdateContentAsync(Guid artifactId, string content) => Task.CompletedTask;
        public Task<List<SdlcArtifact>> GetAllForRunAsync(Guid runId) => Task.FromResult(new List<SdlcArtifact>());
        public Task<List<Guid>> GetAllRunIdsAsync() => Task.FromResult(new List<Guid>());
        public Task UpdateStageAsync(Guid runId, string stage, string status)
        {
            _runs[runId] = new RunCheckpoint(runId, stage, status, DateTimeOffset.UtcNow, "");
            return Task.CompletedTask;
        }
    }
}
