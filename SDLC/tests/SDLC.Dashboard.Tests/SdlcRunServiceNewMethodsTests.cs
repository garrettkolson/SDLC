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
public class SdlcRunServiceNewMethodsTests
{
    private TestArtifactStore _artifactStore = null!;
    private TestGateStore _gateStore = null!;
    private TestRunStore _runStore = null!;
    private CapturingRunner _runner = null!;
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
        _runner = new CapturingRunner();
        telemetry = Substitute.For<IPipelineTelemetry>();
        budgetTracker = Substitute.For<IRunBudgetTracker>();
        budgetTracker.GetUsageAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                     .Returns(Task.FromResult(TokenUsage.Zero));
        budgetTracker.BudgetLimit.Returns(500_000L);
    }

    [Test]
    public async Task StartRunAsync_CallsPipelineRunner()
    {
        var config = new SdlcRunConfig { ProjectBrief = "test brief" };
        var service = new SdlcRunService(_artifactStore, _gateStore, _runStore, telemetry, _runner, budgetTracker);

        var runId = await service.StartRunAsync(config);

        runId.Should().Be(config.RunId);
        _runner.EnqueuedConfig.Should().NotBeNull();
        _runner.EnqueuedConfig!.ProjectBrief.Should().Be("test brief");
    }

    [Test]
    public async Task StartRunAsync_RecordsTelemetry()
    {
        var config = new SdlcRunConfig { ProjectBrief = "test brief" };
        var service = new SdlcRunService(_artifactStore, _gateStore, _runStore, telemetry, _runner, budgetTracker);

        await service.StartRunAsync(config);

        await telemetry.Received(1).StartPipelineRunAsync(config.RunId, config.ProjectBrief, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task GetGateDetailAsync_ReturnsSummary()
    {
        var gateId = Guid.NewGuid();
        var gate = new StageGate
        {
            GateId = gateId,
            RunId = _testRunId,
            Stage = SdlcStage.Requirements,
            Status = GateStatus.Pending,
            Notes = "review needed"
        };
        _gateStore.Gates[gateId] = gate;

        var service = new SdlcRunService(_artifactStore, _gateStore, _runStore, telemetry, _runner, budgetTracker);
        var result = await service.GetGateDetailAsync(gateId);

        result.Should().NotBeNull();
        result!.GateId.Should().Be(gateId);
        result.Stage.Should().Be(SdlcStage.Requirements);
        result.Status.Should().Be(GateStatus.Pending);
        result.Notes.Should().Be("review needed");
    }

    [Test]
    public async Task GetGateDetailAsync_ReturnsNull_WhenNotFound()
    {
        var service = new SdlcRunService(_artifactStore, _gateStore, _runStore, telemetry, _runner, budgetTracker);
        var result = await service.GetGateDetailAsync(Guid.NewGuid());
        result.Should().BeNull();
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

        public Task SaveAsync(SdlcArtifact artifact) { Add(artifact); return Task.CompletedTask; }
        public Task<T?> GetAsync<T>(Guid artifactId) where T : SdlcArtifact =>
            Task.FromResult(_artifacts.OfType<T>().FirstOrDefault(a => a.ArtifactId == artifactId));
        public Task<T?> GetLatestForRunAsync<T>(Guid runId) where T : SdlcArtifact =>
            Task.FromResult(_artifacts.Where(a => a.RunId == runId).OfType<T>().MaxBy(a => a.CreatedAt));
        public Task UpdateStatusAsync(Guid artifactId, ArtifactStatus status) => throw new NotImplementedException();
        public Task UpdateContentAsync(Guid artifactId, string content) => throw new NotImplementedException();
        public Task<List<SdlcArtifact>> GetAllForRunAsync(Guid runId) =>
            Task.FromResult(_byRun.TryGetValue(runId, out var list) ? list : new List<SdlcArtifact>());
        public Task<List<Guid>> GetAllRunIdsAsync() => Task.FromResult(_byRun.Keys.ToList());
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

    private class CapturingRunner() : PipelineRunnerService(null!, null!, null!, Substitute.For<IStageGateStore>(), Substitute.For<IRunStore>())
    {
        public SdlcRunConfig? EnqueuedConfig { get; private set; }
        public Guid EnqueuedRunId => EnqueuedConfig?.RunId ?? Guid.Empty;

        public override async Task EnqueueAsync(SdlcRunConfig config, CancellationToken ct = default)
        {
            EnqueuedConfig = config;
            _activeRuns[config.RunId] = true;
        }

        private readonly Dictionary<Guid, bool> _activeRuns = new();
        public override bool IsRunActive(Guid runId) => _activeRuns.ContainsKey(runId);
        public override Task ResumeGateAsync(Guid runId, Guid gateId, GateDecision decision, string? notes, CancellationToken ct = default)
        {
            _activeRuns.Remove(runId);
            return Task.CompletedTask;
        }
    }

    private class TestRunStore : IRunStore
    {
        private readonly ConcurrentDictionary<Guid, RunCheckpoint> _runs = new();

        public Task InitializeAsync() => Task.CompletedTask;
        public Task CancelRunAsync(Guid runId) => Task.CompletedTask;

        public Task CreateRunAsync(Guid runId, string projectBrief, string startedAt)
        {
            _runs[runId] = new RunCheckpoint(runId, "Research", "Running", DateTimeOffset.Parse(startedAt));
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
            _runs[runId] = new RunCheckpoint(runId, stage, status, DateTimeOffset.UtcNow);
            return Task.CompletedTask;
        }
    }
}
