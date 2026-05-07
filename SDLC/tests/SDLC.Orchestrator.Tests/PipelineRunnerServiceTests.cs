using System.Diagnostics;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NUnit.Framework;
using SDLC.Contracts;
using SDLC.Infrastructure;
using SDLC.Notifications;
using SDLC.Telemetry;

namespace SDLC.Orchestrator.Tests;

[TestFixture, SingleThreaded]
public class PipelineRunnerServiceTests
{
    private static IStageGateStore CreateGateStoreStub() => Substitute.For<IStageGateStore>();
    private static IRunStore CreateRunStoreStub() => Substitute.For<IRunStore>();

    [Test]
    public async Task EnqueueAsync_AddsRunToActiveRuns()
    {
        var processFactory = Substitute.For<ISdlcProcessFactory>();
        processFactory.StartAsync(Arg.Any<SdlcRunConfig>(), Arg.Any<CancellationToken>())
            .Returns(new ProcessHandle(new TaskCompletionSource<Task>().Task));
        var logger = Substitute.For<ILogger<PipelineRunnerService>>();
        var telemetry = Substitute.For<IPipelineTelemetry>();
        telemetry.StartRunActivity(Arg.Any<Guid>()).Returns((Activity?)null);
        var runner = new PipelineRunnerService(processFactory, logger, telemetry, CreateGateStoreStub(), CreateRunStoreStub());
        var config = new SdlcRunConfig { ProjectBrief = "Test project" };

        await runner.EnqueueAsync(config);

        runner.ActiveRunCount.Should().Be(1);
    }

    [Test]
    public async Task EnqueueAsync_MultipleRuns_AllTracked()
    {
        var processFactory = Substitute.For<ISdlcProcessFactory>();
        processFactory.StartAsync(Arg.Any<SdlcRunConfig>(), Arg.Any<CancellationToken>())
            .Returns(new ProcessHandle(new TaskCompletionSource<Task>().Task));
        var logger = Substitute.For<ILogger<PipelineRunnerService>>();
        var telemetry = Substitute.For<IPipelineTelemetry>();
        telemetry.StartRunActivity(Arg.Any<Guid>()).Returns((Activity?)null);
        var runner = new PipelineRunnerService(processFactory, logger, telemetry, CreateGateStoreStub(), CreateRunStoreStub());
        await runner.EnqueueAsync(new SdlcRunConfig { ProjectBrief = "Project A" });
        await runner.EnqueueAsync(new SdlcRunConfig { ProjectBrief = "Project B" });
        await runner.EnqueueAsync(new SdlcRunConfig { ProjectBrief = "Project C" });

        runner.ActiveRunCount.Should().Be(3);
    }

    [Test]
    public async Task IsRunActive_ForEnqueuedRun_ReturnsTrue()
    {
        var processFactory = Substitute.For<ISdlcProcessFactory>();
        processFactory.StartAsync(Arg.Any<SdlcRunConfig>(), Arg.Any<CancellationToken>())
            .Returns(new ProcessHandle(new TaskCompletionSource<Task>().Task));
        var logger = Substitute.For<ILogger<PipelineRunnerService>>();
        var telemetry = Substitute.For<IPipelineTelemetry>();
        telemetry.StartRunActivity(Arg.Any<Guid>()).Returns((Activity?)null);
        var runner = new PipelineRunnerService(processFactory, logger, telemetry, CreateGateStoreStub(), CreateRunStoreStub());
        var config = new SdlcRunConfig { ProjectBrief = "Test" };

        await runner.EnqueueAsync(config);

        runner.IsRunActive(config.RunId).Should().BeTrue();
    }

    [Test]
    public void IsRunActive_ForUnknownRunId_ReturnsFalse()
    {
        var processFactory = Substitute.For<ISdlcProcessFactory>();
        var logger = Substitute.For<ILogger<PipelineRunnerService>>();
        var telemetry = Substitute.For<IPipelineTelemetry>();
        telemetry.StartRunActivity(Arg.Any<Guid>()).Returns((Activity?)null);
        var runner = new PipelineRunnerService(processFactory, logger, telemetry, CreateGateStoreStub(), CreateRunStoreStub());
        runner.IsRunActive(Guid.NewGuid()).Should().BeFalse();
    }

    [Test]
    public async Task ResumeGateAsync_ForUnknownRunId_ThrowsInvalidOperationException()
    {
        var processFactory = Substitute.For<ISdlcProcessFactory>();
        var logger = Substitute.For<ILogger<PipelineRunnerService>>();
        var telemetry = Substitute.For<IPipelineTelemetry>();
        var runner = new PipelineRunnerService(processFactory, logger, telemetry, CreateGateStoreStub(), CreateRunStoreStub());
        var act = () => runner.ResumeGateAsync(Guid.NewGuid(), Guid.NewGuid(), GateDecision.Approved, null);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Test]
    public async Task EnqueueAsync_SameRunIdTwice_ThrowsInvalidOperationException()
    {
        var processFactory = Substitute.For<ISdlcProcessFactory>();
        processFactory.StartAsync(Arg.Any<SdlcRunConfig>(), Arg.Any<CancellationToken>())
            .Returns(new ProcessHandle(new TaskCompletionSource<Task>().Task));
        var logger = Substitute.For<ILogger<PipelineRunnerService>>();
        var telemetry = Substitute.For<IPipelineTelemetry>();
        telemetry.StartRunActivity(Arg.Any<Guid>()).Returns((Activity?)null);
        var runner = new PipelineRunnerService(processFactory, logger, telemetry, CreateGateStoreStub(), CreateRunStoreStub());
        var runId = Guid.NewGuid();
        var config1 = new SdlcRunConfig { RunId = runId, ProjectBrief = "Test" };
        await runner.EnqueueAsync(config1);

        var config2 = new SdlcRunConfig { RunId = runId, ProjectBrief = "Test" };
        var act = () => runner.EnqueueAsync(config2);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Test]
    public async Task CancelRunAsync_UnknownRunId_ThrowsInvalidOperationException()
    {
        var processFactory = Substitute.For<ISdlcProcessFactory>();
        var logger = Substitute.For<ILogger<PipelineRunnerService>>();
        var telemetry = Substitute.For<IPipelineTelemetry>();
        var runner = new PipelineRunnerService(processFactory, logger, telemetry, CreateGateStoreStub(), CreateRunStoreStub());
        var act = () => runner.CancelRunAsync(Guid.NewGuid());

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Test]
    public async Task CancelRunAsync_ForActiveRun_ReturnsWithoutThrowing()
    {
        var processFactory = Substitute.For<ISdlcProcessFactory>();
        var tcs = new TaskCompletionSource<Task>();
        processFactory.StartAsync(Arg.Any<SdlcRunConfig>(), Arg.Any<CancellationToken>())
            .Returns(new ProcessHandle(tcs.Task));
        var logger = Substitute.For<ILogger<PipelineRunnerService>>();
        var telemetry = Substitute.For<IPipelineTelemetry>();
        telemetry.StartRunActivity(Arg.Any<Guid>()).Returns((Activity?)null);
        var runner = new PipelineRunnerService(processFactory, logger, telemetry, CreateGateStoreStub(), CreateRunStoreStub());
        var runId = Guid.NewGuid();
        var config = new SdlcRunConfig { RunId = runId, ProjectBrief = "Test" };

        await runner.EnqueueAsync(config);
        runner.IsRunActive(runId).Should().BeTrue();

        await runner.CancelRunAsync(runId);

        runner.IsRunActive(runId).Should().BeTrue(); // _activeRuns not cleaned up by CancelRunAsync
    }

    [Test]
    public async Task CancelRunAsync_RemovesFromRunCancellation()
    {
        var processFactory = Substitute.For<ISdlcProcessFactory>();
        processFactory.StartAsync(Arg.Any<SdlcRunConfig>(), Arg.Any<CancellationToken>())
            .Returns(new ProcessHandle(new TaskCompletionSource<Task>().Task));
        var logger = Substitute.For<ILogger<PipelineRunnerService>>();
        var telemetry = Substitute.For<IPipelineTelemetry>();
        telemetry.StartRunActivity(Arg.Any<Guid>()).Returns((Activity?)null);
        var runner = new PipelineRunnerService(processFactory, logger, telemetry, CreateGateStoreStub(), CreateRunStoreStub());
        var runId = Guid.NewGuid();
        var config = new SdlcRunConfig { RunId = runId, ProjectBrief = "Test" };

        await runner.EnqueueAsync(config);

        var field = typeof(PipelineRunnerService).GetField("_runCancellation", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var dict = (System.Collections.Concurrent.ConcurrentDictionary<Guid, System.Threading.CancellationTokenSource>)field!.GetValue(runner)!;
        dict.ContainsKey(runId).Should().BeTrue();

        await runner.CancelRunAsync(runId);

        dict.ContainsKey(runId).Should().BeFalse();
    }

    [Test]
    public async Task EnqueueAsync_Throws_WhenProcessFactoryStartAsync_Throws()
    {
        var processFactory = Substitute.For<ISdlcProcessFactory>();
        processFactory.StartAsync(Arg.Any<SdlcRunConfig>(), Arg.Any<CancellationToken>())
            .Returns(x => { throw new InvalidOperationException("factory fail"); });
        var logger = Substitute.For<ILogger<PipelineRunnerService>>();
        var telemetry = Substitute.For<IPipelineTelemetry>();
        telemetry.StartRunActivity(Arg.Any<Guid>()).Returns((Activity?)null);
        var runner = new PipelineRunnerService(processFactory, logger, telemetry, CreateGateStoreStub(), CreateRunStoreStub());
        var config = new SdlcRunConfig { ProjectBrief = "Test" };

        var act = () => runner.EnqueueAsync(config);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
