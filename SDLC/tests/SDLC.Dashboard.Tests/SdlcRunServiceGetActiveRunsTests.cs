using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NUnit.Framework;
using SDLC.Contracts;
using SDLC.Dashboard.Services;
using SDLC.Infrastructure;
using SDLC.Orchestrator;
using SDLC.Telemetry;
using System.Collections.Concurrent;

namespace SDLC.Dashboard.Tests;

/// <summary>
/// GetActiveRunsAsync returns all runs that have artifacts (not hardcoded Guid.Empty).
/// </summary>
[TestFixture, SingleThreaded]
public class SdlcRunServiceGetActiveRunsTests
{
    private TestArtifactStore _artifactStore = null!;
    private TestGateStore _gateStore = null!;
    private TestRunStore _runStore = null!;
    private IPipelineTelemetry telemetry = null!;
    private IRunBudgetTracker budgetTracker = null!;

    [SetUp]
    public void SetUp()
    {
        _artifactStore = new TestArtifactStore();
        _gateStore = new TestGateStore();
        _runStore = new TestRunStore();
        telemetry = Substitute.For<IPipelineTelemetry>();
        budgetTracker = Substitute.For<IRunBudgetTracker>();
        budgetTracker.GetUsageAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                     .Returns(Task.FromResult(TokenUsage.Zero));
        budgetTracker.BudgetLimit.Returns(500_000L);
    }

    [Test]
    public async Task GetActiveRunsAsync_AfterThreeArtifacts_ReturnsThreeSummaries()
    {
        var runId1 = Guid.NewGuid();
        var runId2 = Guid.NewGuid();
        var runId3 = Guid.NewGuid();

        _artifactStore.Add(new ResearchBrief { RunId = runId1, Stage = SdlcStage.Research, Content = "r1" });
        _artifactStore.Add(new RequirementsSpec { RunId = runId2, Stage = SdlcStage.Requirements, Content = "r2" });
        _artifactStore.Add(new ArchitectureRecord { RunId = runId3, Stage = SdlcStage.Design, Content = "r3" });

        var service = new SdlcRunService(_artifactStore, _gateStore, _runStore, telemetry, new TestRunner(), budgetTracker);
        var results = await service.GetActiveRunsAsync();

        results.Should().HaveCount(3);
        results.Select(r => r.RunId).Should().BeEquivalentTo(new[] { runId1, runId2, runId3 });
    }

    [Test]
    public async Task GetActiveRunsAsync_NoArtifacts_ReturnsEmpty()
    {
        var service = new SdlcRunService(_artifactStore, _gateStore, _runStore, telemetry, new TestRunner(), budgetTracker);
        var results = await service.GetActiveRunsAsync();

        results.Should().BeEmpty();
    }

    [Test]
    public async Task GetActiveRunsAsync_MultipleArtifactsSameRun_ReturnsOneSummary()
    {
        var runId = Guid.NewGuid();
        _artifactStore.Add(new ResearchBrief { RunId = runId, Stage = SdlcStage.Research, Content = "r1" });
        _artifactStore.Add(new ResearchBrief { RunId = runId, Stage = SdlcStage.Research, Content = "r2" });

        var service = new SdlcRunService(_artifactStore, _gateStore, _runStore, telemetry, new TestRunner(), budgetTracker);
        var results = await service.GetActiveRunsAsync();

        results.Should().ContainSingle(r => r.RunId == runId);
    }

    private class TestArtifactStore : IArtifactStore
    {
        private readonly List<SdlcArtifact> _artifacts = new();
        public Task InitializeAsync() => Task.CompletedTask;
        public void Add(SdlcArtifact a) => _artifacts.Add(a);
        public Task SaveAsync(SdlcArtifact artifact) { _artifacts.Add(artifact); return Task.CompletedTask; }
        public Task<T?> GetAsync<T>(Guid id) where T : SdlcArtifact => Task.FromResult(_artifacts.OfType<T>().FirstOrDefault(a => a.ArtifactId == id));
        public Task<T?> GetLatestForRunAsync<T>(Guid runId) where T : SdlcArtifact => Task.FromResult(_artifacts.Where(a => a.RunId == runId).OfType<T>().MaxBy(a => a.CreatedAt));
        public Task UpdateStatusAsync(Guid id, ArtifactStatus status) => throw new NotImplementedException();
        public Task UpdateContentAsync(Guid id, string content) => throw new NotImplementedException();
        public Task<List<SdlcArtifact>> GetAllForRunAsync(Guid runId) => Task.FromResult(_artifacts.Where(a => a.RunId == runId).ToList());
        public Task<List<Guid>> GetAllRunIdsAsync() => Task.FromResult(_artifacts.Select(a => a.RunId).Distinct().ToList());
    }

    private class TestGateStore : IStageGateStore
    {
        public Task InitializeAsync() => Task.CompletedTask;
        public Task<StageGate> CreateGateAsync(SdlcArtifact artifact) => throw new NotImplementedException();
        public Task<StageGate?> GetAsync(Guid id) => Task.FromResult<StageGate?>(null);
        public Task ResolveAsync(Guid id, GateDecision decision, string? notes, string resolvedById, string resolvedByDisplay) => throw new NotImplementedException();
        public Task<List<StageGate>> GetPendingForRunAsync(Guid runId) => Task.FromResult(new List<StageGate>());
        public Task<List<StageGate>> GetAllPendingAsync() => Task.FromResult(new List<StageGate>());
    }

    private class TestRunner : IPipelineRunner
    {
        public bool IsRunActive(Guid runId) => false;
        public Task EnqueueAsync(SdlcRunConfig config, CancellationToken ct = default) => Task.CompletedTask;
        public Task ResumeGateAsync(Guid runId, Guid gateId, GateDecision decision, string? notes, CancellationToken ct = default) => Task.CompletedTask;
        public Task CancelRunAsync(Guid runId, CancellationToken ct = default) => Task.CompletedTask;
    }

    private class TestRunStore : IRunStore
    {
        private readonly ConcurrentDictionary<Guid, RunCheckpoint> _runs = new();

        public Task InitializeAsync() => Task.CompletedTask;
        public Task CancelRunAsync(Guid runId) => Task.CompletedTask;

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
