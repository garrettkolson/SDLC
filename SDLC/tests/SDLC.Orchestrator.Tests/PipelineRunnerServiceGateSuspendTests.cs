using System.Diagnostics;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NUnit.Framework;
using SDLC.Contracts;
using SDLC.Infrastructure;
using SDLC.Telemetry;

namespace SDLC.Orchestrator.Tests;

/// <summary>
/// WaitForGateAsync blocks until ResumeGateAsync releases it.
/// </summary>
[TestFixture]
public class PipelineRunnerServiceGateSuspendTests
{
    private static IStageGateStore CreateGateStoreStub() => Substitute.For<IStageGateStore>();
    private static IRunStore CreateRunStoreStub() => Substitute.For<IRunStore>();

    [Test]
    public async Task WaitForGateAsync_BlocksUntilResumeGateAsync()
    {
        var factory = Substitute.For<ISdlcProcessFactory>();
        var logger = Substitute.For<ILogger<PipelineRunnerService>>();
        var telemetry = Substitute.For<IPipelineTelemetry>();
        var runner = new PipelineRunnerService(factory, logger, telemetry, CreateGateStoreStub(), CreateRunStoreStub());

        var runId = Guid.NewGuid();
        var gateId = Guid.NewGuid();
        var config = new SdlcRunConfig { RunId = runId, ProjectBrief = "Test" };

        // Enqueue to establish active run (required by ResumeGateAsync)
        factory.StartAsync(Arg.Any<SdlcRunConfig>(), Arg.Any<CancellationToken>()).Returns(new ProcessHandle(new TaskCompletionSource<Task>().Task));
        await runner.EnqueueAsync(config);

        var waitTask = runner.WaitForGateAsync(gateId, CancellationToken.None);
        waitTask.IsCompleted.Should().BeFalse("should block until resumed");

        await runner.ResumeGateAsync(runId, gateId, GateDecision.Approved, "OK");

        await waitTask;
        waitTask.Result.Decision.Should().Be(GateDecision.Approved);
        waitTask.Result.Notes.Should().Be("OK");
    }

    [Test]
    public async Task WaitForGateAsync_Rejected_ReturnsRejected()
    {
        var factory = Substitute.For<ISdlcProcessFactory>();
        var logger = Substitute.For<ILogger<PipelineRunnerService>>();
        var telemetry = Substitute.For<IPipelineTelemetry>();
        var runner = new PipelineRunnerService(factory, logger, telemetry, CreateGateStoreStub(), CreateRunStoreStub());

        var runId = Guid.NewGuid();
        var gateId = Guid.NewGuid();
        var config = new SdlcRunConfig { RunId = runId, ProjectBrief = "Test" };

        factory.StartAsync(Arg.Any<SdlcRunConfig>(), Arg.Any<CancellationToken>()).Returns(new ProcessHandle(new TaskCompletionSource<Task>().Task));
        await runner.EnqueueAsync(config);

        var waitTask = runner.WaitForGateAsync(gateId, CancellationToken.None);

        await runner.ResumeGateAsync(runId, gateId, GateDecision.Rejected, "Not ready");

        var result = await waitTask;
        result.Decision.Should().Be(GateDecision.Rejected);
        result.Notes.Should().Be("Not ready");
    }

    [Test]
    public async Task WaitForGateAsync_CancellationTokenCanceled_Throws()
    {
        var factory = Substitute.For<ISdlcProcessFactory>();
        var logger = Substitute.For<ILogger<PipelineRunnerService>>();
        var telemetry = Substitute.For<IPipelineTelemetry>();
        var runner = new PipelineRunnerService(factory, logger, telemetry, CreateGateStoreStub(), CreateRunStoreStub());

        var gateId = Guid.NewGuid();
        using var cts = new CancellationTokenSource();
        var waitTask = runner.WaitForGateAsync(gateId, cts.Token);

        cts.Cancel();

        var act = () => waitTask;
        await act.Should().ThrowAsync<TaskCanceledException>();
    }
}
