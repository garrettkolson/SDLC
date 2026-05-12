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
public class SdlcProcessFactoryErrorHandlingTests
{
    private SdlcProcessFactory _factory = null!;
    private IKernelFactory _kernelFactory = null!;
    private IArtifactStore _artifactStore = null!;
    private IStageGateStore _gateStore = null!;
    private INotificationService _notifications = null!;
    private ISweAfClient _sweAfClient = null!;
    private IRunStore _runStore = null!;
    private IPipelineTelemetry _telemetry = null!;
    private PipelineRunnerService _runner = null!;
    private IRunBudgetTracker _budgetTracker = null!;
    private ILogger<SdlcProcessFactory> _logger = null!;

    [SetUp]
    public void SetUp()
    {
        _kernelFactory = Substitute.For<IKernelFactory>();
        _artifactStore = Substitute.For<IArtifactStore>();
        _gateStore = Substitute.For<IStageGateStore>();
        _notifications = Substitute.For<INotificationService>();
        _sweAfClient = Substitute.For<ISweAfClient>();
        _runStore = Substitute.For<IRunStore>();
        _telemetry = Substitute.For<IPipelineTelemetry>();
        _budgetTracker = Substitute.For<IRunBudgetTracker>();
        _budgetTracker.IsOverBudgetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(false);
        _budgetTracker.EnsureWithinBudgetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        _logger = Substitute.For<ILogger<SdlcProcessFactory>>();
        _runner = new PipelineRunnerService(
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

    [Test]
    public async Task StartAsync_StepThrows_UpdatesRunToFailed()
    {
        var runId = Guid.NewGuid();
        var config = new SdlcRunConfig { RunId = runId, ProjectBrief = "Test" };

        _kernelFactory.CreateForStage(SdlcStage.Research)
            .Returns(k => throw new InvalidOperationException("Kernel died"));

        var handle = _factory.StartAsync(config);

        await ((Func<Task>)(() => handle.Task)).Should().ThrowAsync<InvalidOperationException>();

        await _runStore.Received(1).CreateRunAsync(runId, "Test", Arg.Any<string>());
        await _runStore.Received(1).UpdateStageAsync(runId, "Failed", "Failed");
        var errorCalls = _logger.ReceivedCalls().Where(c => c.GetArguments().ElementAtOrDefault(0) is LogLevel l && l == LogLevel.Error).ToList();
        errorCalls.Should().ContainSingle();
    }

    [Test]
    public async Task StartAsync_BudgetExceeded_Throws()
    {
        var runId = Guid.NewGuid();
        var config = new SdlcRunConfig { RunId = runId, ProjectBrief = "Test" };

        _budgetTracker.EnsureWithinBudgetAsync(runId, Arg.Any<CancellationToken>())
            .Returns(Task.FromException<InvalidOperationException>(new InvalidOperationException("Budget exceeded")));

        var handle = _factory.StartAsync(config);

        await ((Func<Task>)(() => handle.Task)).Should().ThrowAsync<InvalidOperationException>();

        await _runStore.Received(1).UpdateStageAsync(runId, "Failed", "Failed");
    }

    [Test]
    public async Task ResumeAsync_StepThrows_UpdatesRunToFailed()
    {
        var runId = Guid.NewGuid();
        var config = new SdlcRunConfig { RunId = runId, ProjectBrief = "Test" };

        _artifactStore.GetLatestForRunAsync<ResearchBrief>(runId).Returns((ResearchBrief?)null);
        _kernelFactory.CreateForStage(SdlcStage.Research)
            .Returns(k => throw new InvalidOperationException("Kernel died"));

        var handle = _factory.ResumeAsync(config, "Research");

        await ((Func<Task>)(() => handle.Task)).Should().ThrowAsync<InvalidOperationException>();

        await _runStore.Received(1).UpdateStageAsync(runId, "Research", "Failed");
    }
}
