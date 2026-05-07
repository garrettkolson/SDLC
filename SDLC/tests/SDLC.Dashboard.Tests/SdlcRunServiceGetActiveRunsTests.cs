using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NUnit.Framework;
using SDLC.Contracts;
using SDLC.Dashboard.Services;
using SDLC.Infrastructure;
using SDLC.Orchestrator;
using SDLC.Telemetry;

namespace SDLC.Dashboard.Tests;

/// <summary>
/// GetActiveRunsAsync returns all runs that have artifacts (not hardcoded Guid.Empty).
/// </summary>
[TestFixture, SingleThreaded]
public class SdlcRunServiceGetActiveRunsTests
{
    private TestArtifactStore _artifactStore = null!;
    private TestGateStore _gateStore = null!;
    private IPipelineTelemetry telemetry = null!;

    [SetUp]
    public void SetUp()
    {
        _artifactStore = new TestArtifactStore();
        _gateStore = new TestGateStore();
        telemetry = Substitute.For<IPipelineTelemetry>();
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

        var service = new SdlcRunService(_artifactStore, _gateStore, telemetry, new TestRunner());
        var results = await service.GetActiveRunsAsync();

        results.Should().HaveCount(3);
        results.Select(r => r.RunId).Should().BeEquivalentTo(new[] { runId1, runId2, runId3 });
    }

    [Test]
    public async Task GetActiveRunsAsync_NoArtifacts_ReturnsEmpty()
    {
        var service = new SdlcRunService(_artifactStore, _gateStore, telemetry, new TestRunner());
        var results = await service.GetActiveRunsAsync();

        results.Should().BeEmpty();
    }

    [Test]
    public async Task GetActiveRunsAsync_MultipleArtifactsSameRun_ReturnsOneSummary()
    {
        var runId = Guid.NewGuid();
        _artifactStore.Add(new ResearchBrief { RunId = runId, Stage = SdlcStage.Research, Content = "r1" });
        _artifactStore.Add(new ResearchBrief { RunId = runId, Stage = SdlcStage.Research, Content = "r2" });

        var service = new SdlcRunService(_artifactStore, _gateStore, telemetry, new TestRunner());
        var results = await service.GetActiveRunsAsync();

        results.Should().ContainSingle(r => r.RunId == runId);
    }

    private class TestArtifactStore : IArtifactStore
    {
        private readonly List<SdlcArtifact> _artifacts = new();
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
    }
}
