using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Diagnostics;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NUnit.Framework;
using SDLC.Agents;
using SDLC.Contracts;
using SDLC.Infrastructure;
using SDLC.Notifications;
using SDLC.Orchestrator;
using SDLC.Telemetry;

namespace SDLC.Integration.Tests;

/// <summary>
/// Full pipeline integration: real SQLite stores, stubbed agent kernels, auto-approving gates.
/// </summary>
[TestFixture]
public class FullPipelineIntegrationTests
{
    private string _dbPath = null!;
    private string _tempDir = null!;
    private ArtifactStore _artifactStore = null!;
    private StageGateStore _gateStore = null!;
    private TestRunStore _runStore = null!;

    [SetUp]
    public async Task SetUp()
    {
        _dbPath = Path.GetTempFileName();
        _tempDir = Path.Combine(Path.GetTempPath(), $"sdlc-full-{Guid.NewGuid():N}");
        _artifactStore = new ArtifactStore(new SqlDbConnectionFactory($"Data Source={_dbPath}"), _tempDir);
        _gateStore = new StageGateStore(new SqlDbConnectionFactory($"Data Source={_dbPath}"));
        _runStore = new TestRunStore();
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
        catch { /* best-effort */ }
    }

    [Test]
    public async Task HappyPath_AllStagesComplete_GateApproved()
    {
        var runId = Guid.NewGuid();
        var config = new SdlcRunConfig { RunId = runId, ProjectBrief = "Test brief" };
        var decisions = new ConcurrentDictionary<Guid, GateDecision>();

        var gate = new AutoApprovingGate(_gateStore, decisions);
        var runner = BuildRunner(gate, decisions);

        await runner.EnqueueAsync(config);

        await WaitForRun(runner, runId, TimeSpan.FromSeconds(10));

        var run = await _runStore.GetRunAsync(runId);
        run.Should().NotBeNull();
        run!.Status.Should().Be("Completed");
    }

    [Test]
    public async Task GateRejected_PipelineAborts()
    {
        var runId = Guid.NewGuid();
        var config = new SdlcRunConfig { RunId = runId, ProjectBrief = "Test brief" };
        var decisions = new ConcurrentDictionary<Guid, GateDecision>();

        var gate = new RejectingGate(_gateStore, decisions);
        var runner = BuildRunner(gate, decisions);

        await runner.EnqueueAsync(config);

        await WaitForRun(runner, runId, TimeSpan.FromSeconds(10));

        var run = await _runStore.GetRunAsync(runId);
        run.Should().NotBeNull();
        run!.Status.Should().Be("Failed");
    }

    [Test]
    public async Task ConcurrentRuns_ArtifactsIsolatedByRunId()
    {
        var runId1 = Guid.NewGuid();
        var runId2 = Guid.NewGuid();
        var config1 = new SdlcRunConfig { RunId = runId1, ProjectBrief = "Run 1" };
        var config2 = new SdlcRunConfig { RunId = runId2, ProjectBrief = "Run 2" };
        var decisions = new ConcurrentDictionary<Guid, GateDecision>();

        var gate = new AutoApprovingGate(_gateStore, decisions);
        var runner = BuildRunner(gate, decisions);

        await runner.EnqueueAsync(config1);
        await runner.EnqueueAsync(config2);

        await Task.WhenAll(
            WaitForRun(runner, runId1, TimeSpan.FromSeconds(10)),
            WaitForRun(runner, runId2, TimeSpan.FromSeconds(10)));

        var run1 = await _runStore.GetRunAsync(runId1);
        var run2 = await _runStore.GetRunAsync(runId2);

        run1!.ProjectBrief.Should().Be("Run 1");
        run2!.ProjectBrief.Should().Be("Run 2");
    }

    private PipelineRunnerService BuildRunner(IGateResolver gate, ConcurrentDictionary<Guid, GateDecision> decisions)
    {
        var factory = BuildFactory(gate, decisions);
        var logger = Substitute.For<ILogger<PipelineRunnerService>>();
        var telemetry = Substitute.For<IPipelineTelemetry>();
        telemetry.StartRunActivity(Arg.Any<Guid>()).Returns((Activity?)null);
        var budgetTracker = Substitute.For<IRunBudgetTracker>();
        budgetTracker.IsOverBudgetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(false);
        budgetTracker.EnsureWithinBudgetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        return new PipelineRunnerService(factory, logger, telemetry, _gateStore, _runStore, budgetTracker);
    }

    private SdlcProcessFactory BuildFactory(IGateResolver gate, ConcurrentDictionary<Guid, GateDecision> decisions)
    {
        var kernels = new Dictionary<SdlcStage, IKernel>();
        foreach (SdlcStage stage in Enum.GetValues<SdlcStage>())
            kernels[stage] = CreateSucceedKernel(stage);

        var kernelFactory = Substitute.For<IKernelFactory>();
        foreach (var (stage, kernel) in kernels)
            kernelFactory.CreateForStage(stage).Returns(kernel);

        var notifications = new MockNotificationService(gate, decisions);
        var sweAfClient = new StubSweAfClient();
        var telemetry = Substitute.For<IPipelineTelemetry>();
        telemetry.StartRunActivity(Arg.Any<Guid>()).Returns((Activity?)null);
        var runner = Substitute.For<PipelineRunnerService>(
            Substitute.For<ISdlcProcessFactory>(),
            Substitute.For<ILogger<PipelineRunnerService>>(),
            Substitute.For<IPipelineTelemetry>(),
            Substitute.For<IStageGateStore>(),
            Substitute.For<IRunStore>(),
            Substitute.For<IRunBudgetTracker>());
        runner.WaitForGateAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var gateId = callInfo.ArgAt<Guid>(0);
                var decision = decisions.GetValueOrDefault(gateId, GateDecision.Approved);
                return Task.FromResult(new GateResolution(gateId, decision, decision == GateDecision.Rejected ? "Rejected" : null));
            });
        var loggerFactory = Substitute.For<ILoggerFactory>();
        var logger = Substitute.For<ILogger<SdlcProcessFactory>>();

        return new SdlcProcessFactory(
            kernelFactory, _artifactStore, _gateStore, notifications, sweAfClient,
            _runStore, telemetry, runner, Substitute.For<IRunBudgetTracker>(),
            loggerFactory, logger);
    }

    private static IKernel CreateSucceedKernel(SdlcStage stage)
    {
        var kernel = Substitute.For<IKernel>();
        kernel.CompleteAsyncWithUsage(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(("response", new TokenUsage(10L, 5L))));
        return kernel;
    }

    private async Task WaitForRun(PipelineRunnerService runner, Guid runId, TimeSpan timeout)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < timeout)
        {
            var cp = await _runStore.GetRunAsync(runId);
            if (cp != null && (cp.Status == "Completed" || cp.Status == "Failed"))
                return;
            await Task.Delay(100);
        }
    }

    private interface IGateResolver
    {
        Task<GateResolution> Resolve(StageGate gate, CancellationToken ct);
    }

    private class AutoApprovingGate : IGateResolver
    {
        private readonly IStageGateStore _store;
        private readonly ConcurrentDictionary<Guid, GateDecision> _decisions;
        public AutoApprovingGate(IStageGateStore store, ConcurrentDictionary<Guid, GateDecision> decisions)
        { _store = store; _decisions = decisions; }

        public async Task<GateResolution> Resolve(StageGate gate, CancellationToken ct)
        {
            await _store.ResolveAsync(gate.GateId, GateDecision.Approved, null, "auto", "Auto");
            _decisions[gate.GateId] = GateDecision.Approved;
            return new GateResolution(gate.GateId, GateDecision.Approved, null);
        }
    }

    private class RejectingGate : IGateResolver
    {
        private readonly IStageGateStore _store;
        private readonly ConcurrentDictionary<Guid, GateDecision> _decisions;
        public RejectingGate(IStageGateStore store, ConcurrentDictionary<Guid, GateDecision> decisions)
        { _store = store; _decisions = decisions; }

        public async Task<GateResolution> Resolve(StageGate gate, CancellationToken ct)
        {
            await _store.ResolveAsync(gate.GateId, GateDecision.Rejected, "Rejected", "auto", "Auto");
            _decisions[gate.GateId] = GateDecision.Rejected;
            return new GateResolution(gate.GateId, GateDecision.Rejected, "Rejected");
        }
    }

    private class MockNotificationService : INotificationService
    {
        private readonly IGateResolver _resolver;
        private readonly ConcurrentDictionary<Guid, GateDecision> _decisions;
        public MockNotificationService(IGateResolver resolver, ConcurrentDictionary<Guid, GateDecision> decisions)
        { _resolver = resolver; _decisions = decisions; }

        public async Task SendApprovalRequestAsync(StageGate gate)
        {
            await Task.Delay(50);
            var resolution = await _resolver.Resolve(gate, CancellationToken.None);
            _decisions[gate.GateId] = resolution.Decision;
        }
    }

    private class StubSweAfClient : ISweAfClient
    {
        public Task<string> SubmitAsync(SweAfTask task, CancellationToken ct = default)
            => Task.FromResult("stub-run-id");

        public async IAsyncEnumerable<SweAfStatus> PollAsync(string runId, [EnumeratorCancellation] CancellationToken ct = default)
        {
            yield return new SweAfStatus { State = SweAfState.Succeeded, IsTerminal = true, Logs = "stub" };
        }
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
