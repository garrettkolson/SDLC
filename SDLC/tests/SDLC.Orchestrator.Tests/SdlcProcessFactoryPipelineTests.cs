using System.Runtime.CompilerServices;
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
public class SdlcProcessFactoryPipelineTests
{
    private SdlcProcessFactory _factory = null!;
    private IKernelFactory _kernelFactory = null!;
    private CapturingSweAfClient _sweAfClient = null!;
    private TestArtifactStore _artifactStore = null!;
    private TestStageGateStore _gateStore = null!;
    private INotificationService _notifications = null!;
    private TestRunStore _runStore = null!;
    private IPipelineTelemetry _telemetry = null!;
    private PipelineRunnerService _runner = null!;
    private IRunBudgetTracker _budgetTracker = null!;
    private ILogger<SdlcProcessFactory> _logger = null!;

    [SetUp]
    public void SetUp()
    {
        _kernelFactory = Substitute.For<IKernelFactory>();
        _sweAfClient = new CapturingSweAfClient();
        _artifactStore = new TestArtifactStore();
        _gateStore = new TestStageGateStore();
        _notifications = Substitute.For<INotificationService>();
        _runStore = new TestRunStore();
        _telemetry = Substitute.For<IPipelineTelemetry>();
        _budgetTracker = Substitute.For<IRunBudgetTracker>();
        _budgetTracker.BudgetLimit.Returns(100000L);
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
            _kernelFactory, _artifactStore, _gateStore, _notifications, _sweAfClient,
            _runStore, _telemetry, _runner, _budgetTracker,
            Substitute.For<ILoggerFactory>(), _logger);
    }

    private void SetGateApproval(Guid gateId, Guid runId, GateDecision decision)
    {
        var resolution = new GateResolution(gateId, decision, decision == GateDecision.Rejected ? "No" : null);
        _runner.WaitForGateAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(resolution);
    }

    [Test]
    public async Task StartAsync_CallsStagesInCorrectOrder()
    {
        var runId = Guid.NewGuid();
        var config = new SdlcRunConfig { RunId = runId, ProjectBrief = "Test project" };
        var research = new ResearchBrief { RunId = runId, Stage = SdlcStage.Research };
        var spec = new RequirementsSpec { RunId = runId, Stage = SdlcStage.Requirements };
        var arch = new ArchitectureRecord { RunId = runId, Stage = SdlcStage.Design };
        var learnReport = new LearnReport { RunId = runId, Stage = SdlcStage.Learn };

        _artifactStore.SetLatest(runId, (SdlcArtifact)spec);
        IKernel researchKernel = CreateKernel(research);
        IKernel reqKernel = CreateKernel(spec);
        IKernel designKernel = CreateKernel(arch);
        IKernel learnKernel = CreateKernel(learnReport);
        _kernelFactory.CreateForStage(SdlcStage.Research).Returns(researchKernel);
        _kernelFactory.CreateForStage(SdlcStage.Requirements).Returns(reqKernel);
        _kernelFactory.CreateForStage(SdlcStage.Design).Returns(designKernel);
        _kernelFactory.CreateForStage(SdlcStage.Learn).Returns(learnKernel);
        _sweAfClient.SetResult(new SweAfStatus { State = SweAfState.Succeeded, IsTerminal = true, Logs = "OK" });

        SetGateApproval(Guid.Empty, runId, GateDecision.Approved);

        var handle = _factory.StartAsync(config);
        await handle.Task;

        _runStore.Updates.Should().ContainInOrder(
            (runId, "Research", "Running"),
            (runId, "Requirements", "Running"),
            (runId, "Design", "Running"),
            (runId, "Build", "Running"),
            (runId, "Learn", "Completed"));
    }

    [Test]
    public async Task StartAsync_CallsGates()
    {
        var runId = Guid.NewGuid();
        var config = new SdlcRunConfig { RunId = runId, ProjectBrief = "Test project" };
        var research = new ResearchBrief { RunId = runId, Stage = SdlcStage.Research };
        var spec = new RequirementsSpec { RunId = runId, Stage = SdlcStage.Requirements };
        var arch = new ArchitectureRecord { RunId = runId, Stage = SdlcStage.Design };
        var learnReport = new LearnReport { RunId = runId, Stage = SdlcStage.Learn };

        _artifactStore.SetLatest(runId, (SdlcArtifact)spec);
        IKernel rk = CreateKernel(research);
        IKernel rqs = CreateKernel(spec);
        IKernel dk = CreateKernel(arch);
        IKernel lk = CreateKernel(learnReport);
        _kernelFactory.CreateForStage(SdlcStage.Research).Returns(rk);
        _kernelFactory.CreateForStage(SdlcStage.Requirements).Returns(rqs);
        _kernelFactory.CreateForStage(SdlcStage.Design).Returns(dk);
        _kernelFactory.CreateForStage(SdlcStage.Learn).Returns(lk);
        _sweAfClient.SetResult(new SweAfStatus { State = SweAfState.Succeeded, IsTerminal = true, Logs = "OK" });

        SetGateApproval(Guid.Empty, runId, GateDecision.Approved);

        var handle = _factory.StartAsync(config);
        await handle.Task;

        _gateStore.Gates.Should().HaveCount(2);
        await _runner.Received(2).WaitForGateAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task StartAsync_SendsNotificationAtGates()
    {
        var runId = Guid.NewGuid();
        var config = new SdlcRunConfig { RunId = runId, ProjectBrief = "Test project" };
        var research = new ResearchBrief { RunId = runId, Stage = SdlcStage.Research };
        var spec = new RequirementsSpec { RunId = runId, Stage = SdlcStage.Requirements };
        var arch = new ArchitectureRecord { RunId = runId, Stage = SdlcStage.Design };
        var learnReport = new LearnReport { RunId = runId, Stage = SdlcStage.Learn };

        _artifactStore.SetLatest(runId, (SdlcArtifact)spec);
        IKernel rk2 = CreateKernel(research);
        IKernel rqs2 = CreateKernel(spec);
        IKernel dk2 = CreateKernel(arch);
        IKernel lk2 = CreateKernel(learnReport);
        _kernelFactory.CreateForStage(SdlcStage.Research).Returns(rk2);
        _kernelFactory.CreateForStage(SdlcStage.Requirements).Returns(rqs2);
        _kernelFactory.CreateForStage(SdlcStage.Design).Returns(dk2);
        _kernelFactory.CreateForStage(SdlcStage.Learn).Returns(lk2);
        _sweAfClient.SetResult(new SweAfStatus { State = SweAfState.Succeeded, IsTerminal = true, Logs = "OK" });

        SetGateApproval(Guid.Empty, runId, GateDecision.Approved);

        var handle = _factory.StartAsync(config);
        await handle.Task;

        await _notifications.Received(2).SendApprovalRequestAsync(Arg.Any<StageGate>());
    }

    [Test]
    public async Task StartAsync_SavesArtifactToStore()
    {
        var runId = Guid.NewGuid();
        var config = new SdlcRunConfig { RunId = runId, ProjectBrief = "Test project" };
        var research = new ResearchBrief { RunId = runId, Stage = SdlcStage.Research };
        var spec = new RequirementsSpec { RunId = runId, Stage = SdlcStage.Requirements };
        var arch = new ArchitectureRecord { RunId = runId, Stage = SdlcStage.Design };
        var learnReport = new LearnReport { RunId = runId, Stage = SdlcStage.Learn };

        _artifactStore.SetLatest(runId, (SdlcArtifact)spec);
        IKernel rk3 = CreateKernel(research);
        IKernel rqs3 = CreateKernel(spec);
        IKernel dk3 = CreateKernel(arch);
        IKernel lk3 = CreateKernel(learnReport);
        _kernelFactory.CreateForStage(SdlcStage.Research).Returns(rk3);
        _kernelFactory.CreateForStage(SdlcStage.Requirements).Returns(rqs3);
        _kernelFactory.CreateForStage(SdlcStage.Design).Returns(dk3);
        _kernelFactory.CreateForStage(SdlcStage.Learn).Returns(lk3);
        _sweAfClient.SetResult(new SweAfStatus { State = SweAfState.Succeeded, IsTerminal = true, Logs = "OK" });

        SetGateApproval(Guid.Empty, runId, GateDecision.Approved);

        var handle = _factory.StartAsync(config);
        await handle.Task;

        _artifactStore.Saved.Should().ContainSingle(a => a is ResearchBrief);
        _artifactStore.Saved.Should().ContainSingle(a => a is RequirementsSpec);
        _artifactStore.Saved.Should().ContainSingle(a => a is ArchitectureRecord);
    }

    private static IKernel CreateKernel(SdlcArtifact artifact)
    {
        var kernel = Substitute.For<IKernel>();
        kernel.CompleteAsyncWithUsage(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(("response", new TokenUsage(10L, 5L))));
        return kernel;
    }

    private class CapturingSweAfClient : ISweAfClient
    {
        private readonly List<SweAfStatus> _statuses = new();

        public void SetResult(SweAfStatus status) => _statuses.Add(status);

        public Task<string> SubmitAsync(SweAfTask task, CancellationToken ct = default) => Task.FromResult("run-001");

        public async IAsyncEnumerable<SweAfStatus> PollAsync(string runId, [EnumeratorCancellation] CancellationToken ct = default)
        {
            foreach (var s in _statuses)
            {
                yield return s;
                if (s.IsTerminal) yield break;
            }
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
            yield break;
        }
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
        {
            _runs[runId] = new RunCheckpoint(runId, "Research", "Running", DateTimeOffset.UtcNow, projectBrief);
            return Task.CompletedTask;
        }
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
