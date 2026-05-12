using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NUnit.Framework;
using SDLC.Agents;
using SDLC.Contracts;
using SDLC.Infrastructure;
using SDLC.Notifications;
using SDLC.Telemetry;

namespace SDLC.Orchestrator.Tests;

[TestFixture, SingleThreaded]
public class SdlcProcessFactoryResumeTests
{
    private SdlcProcessFactory _factory = null!;
    private IKernelFactory _kernelFactory = null!;
    private TestArtifactStore _artifactStore = null!;
    private TestRunStore _runStore = null!;
    private IPipelineTelemetry _telemetry = null!;
    private PipelineRunnerService _runner = null!;
    private IRunBudgetTracker _budgetTracker = null!;
    private ILogger<SdlcProcessFactory> _logger = null!;

    [SetUp]
    public void SetUp()
    {
        _kernelFactory = Substitute.For<IKernelFactory>();
        _artifactStore = new TestArtifactStore();
        _runStore = new TestRunStore();
        _telemetry = Substitute.For<IPipelineTelemetry>();
        _budgetTracker = Substitute.For<IRunBudgetTracker>();
        _budgetTracker.IsOverBudgetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(false);
        _budgetTracker.EnsureWithinBudgetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        _logger = Substitute.For<ILogger<SdlcProcessFactory>>();
        _runner = Substitute.For<PipelineRunnerService>(
            Substitute.For<ISdlcProcessFactory>(),
            Substitute.For<ILogger<PipelineRunnerService>>(),
            Substitute.For<IPipelineTelemetry>(),
            Substitute.For<IStageGateStore>(),
            Substitute.For<IRunStore>(),
            Substitute.For<IRunBudgetTracker>());

        _factory = new SdlcProcessFactory(
            _kernelFactory, _artifactStore, Substitute.For<IStageGateStore>(),
            Substitute.For<INotificationService>(), Substitute.For<ISweAfClient>(),
            _runStore, _telemetry, _runner, _budgetTracker,
            Substitute.For<ILoggerFactory>(), _logger);
    }

    [Test]
    public async Task ResumeAsync_ResearchStage_UpdatesStageToCompleted()
    {
        var runId = Guid.NewGuid();
        var config = new SdlcRunConfig { RunId = runId, ProjectBrief = "Test" };
        _artifactStore.SetLatest(runId, (SdlcArtifact)new ResearchBrief { RunId = runId, Stage = SdlcStage.Research });
        _artifactStore.SetLatest(runId, (SdlcArtifact)new RequirementsSpec { RunId = runId, Stage = SdlcStage.Requirements });
        IKernel researchKernel = CreateKernel(new ResearchBrief { RunId = runId, Stage = SdlcStage.Research });
        IKernel reqKernel = CreateKernel(new RequirementsSpec { RunId = runId, Stage = SdlcStage.Requirements });
        _kernelFactory.CreateForStage(SdlcStage.Research).Returns(researchKernel);
        _kernelFactory.CreateForStage(SdlcStage.Requirements).Returns(reqKernel);

        var handle = _factory.ResumeAsync(config, "Research");
        await handle.Task;

        _runStore.Updates.Should().ContainSingle(u => u.Stage == "Research" && u.Status == "Completed");
    }

    [Test]
    public async Task ResumeAsync_RequirementsStage_UpdatesStageToCompleted()
    {
        var runId = Guid.NewGuid();
        var config = new SdlcRunConfig { RunId = runId, ProjectBrief = "Test" };
        _artifactStore.SetLatest(runId, (SdlcArtifact)new ResearchBrief { RunId = runId, Stage = SdlcStage.Research });
        IKernel rk = CreateKernel(new ResearchBrief { RunId = runId, Stage = SdlcStage.Research });
        IKernel rqs = CreateKernel(new RequirementsSpec { RunId = runId, Stage = SdlcStage.Requirements });
        _kernelFactory.CreateForStage(SdlcStage.Research).Returns(rk);
        _kernelFactory.CreateForStage(SdlcStage.Requirements).Returns(rqs);

        var handle = _factory.ResumeAsync(config, "Requirements");
        await handle.Task;

        _runStore.Updates.Should().ContainSingle(u => u.Stage == "Requirements" && u.Status == "Completed");
    }

    [Test]
    public async Task ResumeAsync_DesignStage_RunsDesignStep()
    {
        var runId = Guid.NewGuid();
        var config = new SdlcRunConfig { RunId = runId, ProjectBrief = "Test" };
        var research = new ResearchBrief { RunId = runId, Stage = SdlcStage.Research };
        var spec = new RequirementsSpec { RunId = runId, Stage = SdlcStage.Requirements };
        var arch = new ArchitectureRecord { RunId = runId, Stage = SdlcStage.Design };

        _artifactStore.SetLatest(runId, (SdlcArtifact)research);
        _artifactStore.SetLatest(runId, (SdlcArtifact)spec);
        IKernel dk = CreateKernel(arch);
        _kernelFactory.CreateForStage(SdlcStage.Design).Returns(dk);

        var handle = _factory.ResumeAsync(config, "Design");
        await handle.Task;

        _runStore.Updates.Should().ContainSingle(u => u.Stage == "Design" && u.Status == "Completed");
    }

    [Test]
    public async Task ResumeAsync_BuildStage_RunsBuildStep()
    {
        var runId = Guid.NewGuid();
        var config = new SdlcRunConfig { RunId = runId, ProjectBrief = "Test" };
        var spec = new RequirementsSpec { RunId = runId, Stage = SdlcStage.Requirements };
        var arch = new ArchitectureRecord { RunId = runId, Stage = SdlcStage.Design };

        _artifactStore.SetLatest(runId, (SdlcArtifact)new ResearchBrief { RunId = runId, Stage = SdlcStage.Research });
        _artifactStore.SetLatest(runId, (SdlcArtifact)spec);
        _artifactStore.SetLatest(runId, (SdlcArtifact)arch);

        var handle = _factory.ResumeAsync(config, "Build");
        await handle.Task;

        _runStore.Updates.Should().ContainSingle(u => u.Stage == "Build" && u.Status == "Completed");
    }

    [Test]
    public async Task ResumeAsync_LearnStage_RunsBuildAndLearnSteps()
    {
        var runId = Guid.NewGuid();
        var config = new SdlcRunConfig { RunId = runId, ProjectBrief = "Test" };
        var spec = new RequirementsSpec { RunId = runId, Stage = SdlcStage.Requirements };
        var arch = new ArchitectureRecord { RunId = runId, Stage = SdlcStage.Design };
        var learnReport = new LearnReport { RunId = runId, Stage = SdlcStage.Learn };

        _artifactStore.SetLatest(runId, (SdlcArtifact)new ResearchBrief { RunId = runId, Stage = SdlcStage.Research });
        _artifactStore.SetLatest(runId, (SdlcArtifact)spec);
        _artifactStore.SetLatest(runId, (SdlcArtifact)arch);
        IKernel lk = CreateKernel(learnReport);
        _kernelFactory.CreateForStage(SdlcStage.Learn).Returns(lk);

        var handle = _factory.ResumeAsync(config, "Learn");
        await handle.Task;

        _runStore.Updates.Should().ContainSingle(u => u.Stage == "Learn" && u.Status == "Completed");
    }

    [Test]
    public async Task ResumeAsync_LearnStage_ThrowsWhenNoArchitecture()
    {
        var runId = Guid.NewGuid();
        var config = new SdlcRunConfig { RunId = runId, ProjectBrief = "Test" };
        var spec = new RequirementsSpec { RunId = runId, Stage = SdlcStage.Requirements };

        _artifactStore.SetLatest(runId, (SdlcArtifact)new ResearchBrief { RunId = runId, Stage = SdlcStage.Research });
        _artifactStore.SetLatest(runId, (SdlcArtifact)spec);

        var act = () => _factory.ResumeAsync(config, "Learn").Task;

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*No architecture artifact*");
    }

    [Test]
    public async Task ResumeAsync_UnknownStage_ThrowsInvalidOperationException()
    {
        var runId = Guid.NewGuid();
        var config = new SdlcRunConfig { RunId = runId, ProjectBrief = "Test" };

        _artifactStore.SetLatest(runId, (SdlcArtifact)new ResearchBrief { RunId = runId, Stage = SdlcStage.Research });
        _artifactStore.SetLatest(runId, (SdlcArtifact)new RequirementsSpec { RunId = runId, Stage = SdlcStage.Requirements });

        var act = () => _factory.ResumeAsync(config, "UnknownStage").Task;

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Unknown resume stage*");
    }

    private static IKernel CreateKernel(SdlcArtifact artifact)
    {
        var kernel = Substitute.For<IKernel>();
        kernel.CompleteAsyncWithUsage(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(("response", new TokenUsage(10L, 5L))));
        return kernel;
    }

    private class TestArtifactStore : IArtifactStore
    {
        private readonly Dictionary<Guid, Dictionary<Type, SdlcArtifact>> _data = new();
        public List<SdlcArtifact> Saved { get; } = new();

        public void SetLatest(Guid runId, SdlcArtifact artifact)
        {
            if (!_data.ContainsKey(runId)) _data[runId] = new();
            _data[runId][artifact.GetType()] = artifact;
        }

        public Task InitializeAsync() => Task.CompletedTask;
        public Task SaveAsync(SdlcArtifact artifact) { Saved.Add(artifact); return Task.CompletedTask; }
        public Task<T?> GetAsync<T>(Guid artifactId) where T : SdlcArtifact => throw new NotImplementedException();
        public Task<T?> GetLatestForRunAsync<T>(Guid runId) where T : SdlcArtifact
        {
            _data.TryGetValue(runId, out var dict);
            return Task.FromResult<T?>(dict?.Values.OfType<T>().LastOrDefault());
        }
        public Task UpdateStatusAsync(Guid artifactId, ArtifactStatus status) => Task.CompletedTask;
        public Task UpdateContentAsync(Guid artifactId, string content) => Task.CompletedTask;
        public Task<List<SdlcArtifact>> GetAllForRunAsync(Guid runId) => Task.FromResult(_data.GetValueOrDefault(runId)?.Values.ToList() ?? new());
        public Task<List<Guid>> GetAllRunIdsAsync() => Task.FromResult(new List<Guid>());
    }

    private class TestRunStore : IRunStore
    {
        private readonly Dictionary<Guid, RunCheckpoint> _runs = new();
        public List<(Guid RunId, string Stage, string Status)> Updates { get; } = new();

        public Task InitializeAsync() => Task.CompletedTask;
        public Task CreateRunAsync(Guid runId, string projectBrief, string startedAt)
        { _runs[runId] = new RunCheckpoint(runId, "Research", "Running", DateTimeOffset.UtcNow, projectBrief); return Task.CompletedTask; }
        public Task UpdateStageAsync(Guid runId, string stage, string status)
        {
            if (!_runs.ContainsKey(runId)) _runs[runId] = new RunCheckpoint(runId, stage, status, DateTimeOffset.UtcNow, "");
            else _runs[runId] = new RunCheckpoint(runId, stage, status, _runs[runId].StartedAt, _runs[runId].ProjectBrief);
            Updates.Add((runId, stage, status));
            return Task.CompletedTask;
        }
        public Task<RunCheckpoint?> GetRunAsync(Guid runId) => Task.FromResult(_runs.GetValueOrDefault(runId));
        public Task<List<RunCheckpoint>> GetAllIncompleteAsync() => Task.FromResult(_runs.Values.Where(r => r.Status != "Completed").ToList());
        public Task CancelRunAsync(Guid runId) => Task.CompletedTask;
    }
}
