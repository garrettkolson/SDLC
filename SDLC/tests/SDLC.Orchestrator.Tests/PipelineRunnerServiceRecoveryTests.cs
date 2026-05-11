using System.Diagnostics;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NUnit.Framework;
using SDLC.Contracts;
using SDLC.Infrastructure;
using SDLC.Telemetry;

namespace SDLC.Orchestrator.Tests;

[TestFixture, SingleThreaded]
public class PipelineRunnerServiceRecoveryTests
{
    private static IStageGateStore CreateGateStoreStub()
    {
        var stub = Substitute.For<IStageGateStore>();
        stub.GetAllPendingAsync().Returns(Task.FromResult<List<StageGate>>(new List<StageGate>()));
        stub.GetPendingForRunAsync(Arg.Any<Guid>()).Returns(Task.FromResult<List<StageGate>>(new List<StageGate>()));
        return stub;
    }
    private static IRunStore CreateRunStoreStub()
    {
        var stub = Substitute.For<IRunStore>();
        stub.GetAllIncompleteAsync().Returns(Task.FromResult<List<RunCheckpoint>>(new List<RunCheckpoint>()));
        return stub;
    }
    private static ISdlcProcessFactory CreateFactoryStub()
    {
        var stub = Substitute.For<ISdlcProcessFactory>();
        var pending = new TaskCompletionSource<bool>();
        stub.StartAsync(Arg.Any<SdlcRunConfig>(), Arg.Any<CancellationToken>())
            .Returns(new ProcessHandle(pending.Task));
        stub.ResumeAsync(Arg.Any<SdlcRunConfig>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ProcessHandle(pending.Task));
        return stub;
    }

    [Test]
    public async Task RecoverPendingGatesAsync_HydratesPendingGatesFromStore()
    {
        var factory = CreateFactoryStub();
        var logger = Substitute.For<ILogger<PipelineRunnerService>>();
        var telemetry = Substitute.For<IPipelineTelemetry>();
        var gateStore = CreateGateStoreStub();
        var runStore = CreateRunStoreStub();

        var runId = Guid.NewGuid();
        var gateId = Guid.NewGuid();
        var gate = new StageGate { RunId = runId, GateId = gateId, Status = GateStatus.Pending };
        gateStore.GetAllPendingAsync().Returns(Task.FromResult(new List<StageGate> { gate }));
        runStore.GetAllIncompleteAsync().Returns(Task.FromResult(new List<RunCheckpoint>
        {
            new RunCheckpoint(runId, "Design", "Running", DateTimeOffset.UtcNow, "")
        }));
        gateStore.GetPendingForRunAsync(runId).Returns(Task.FromResult(new List<StageGate> { gate }));

        var runner = new PipelineRunnerService(factory, logger, telemetry, gateStore, runStore);

        await runner.RecoverPendingGatesAsync();
        runner.ActiveRunCount.Should().Be(1);
    }

    [Test]
    public async Task RecoverPendingGatesAsync_HydratesActiveRunsFromStore()
    {
        var factory = CreateFactoryStub();
        var logger = Substitute.For<ILogger<PipelineRunnerService>>();
        var telemetry = Substitute.For<IPipelineTelemetry>();
        var gateStore = CreateGateStoreStub();
        var runStore = CreateRunStoreStub();

        var runId = Guid.NewGuid();
        runStore.GetAllIncompleteAsync().Returns(Task.FromResult(new List<RunCheckpoint>
        {
            new RunCheckpoint(runId, "Design", "Running", DateTimeOffset.UtcNow, "")
        }));
        gateStore.GetAllPendingAsync().Returns(Task.FromResult(new List<StageGate>()));

        var runner = new PipelineRunnerService(factory, logger, telemetry, gateStore, runStore);

        await runner.RecoverPendingGatesAsync();

        runner.ActiveRunCount.Should().Be(1);
        runner.GetAllActiveRunIds().Should().Contain(runId);
    }

    [Test]
    public async Task RecoverPendingGatesAsync_EmptyStore_NoOp()
    {
        var factory = CreateFactoryStub();
        var logger = Substitute.For<ILogger<PipelineRunnerService>>();
        var telemetry = Substitute.For<IPipelineTelemetry>();
        var gateStore = CreateGateStoreStub();
        var runStore = CreateRunStoreStub();

        gateStore.GetAllPendingAsync().Returns(Task.FromResult(new List<StageGate>()));
        runStore.GetAllIncompleteAsync().Returns(Task.FromResult(new List<RunCheckpoint>()));

        var runner = new PipelineRunnerService(factory, logger, telemetry, gateStore, runStore);

        await runner.RecoverPendingGatesAsync();

        runner.ActiveRunCount.Should().Be(0);
    }

    [Test]
    public async Task GetAllActiveRunIds_ReturnsActiveRunIds()
    {
        var factory = CreateFactoryStub();
        var logger = Substitute.For<ILogger<PipelineRunnerService>>();
        var telemetry = Substitute.For<IPipelineTelemetry>();
        telemetry.StartRunActivity(Arg.Any<Guid>()).Returns((Activity?)null);
        var runner = new PipelineRunnerService(factory, logger, telemetry, CreateGateStoreStub(), CreateRunStoreStub());

        var runId = Guid.NewGuid();
        runner.IsRunActive(runId).Should().BeFalse();

        await runner.EnqueueAsync(new SdlcRunConfig { RunId = runId, ProjectBrief = "Test" });

        runner.GetAllActiveRunIds().Should().Contain(runId);
        runner.GetAllActiveRunIds().Should().HaveCount(1);
    }

    [Test]
    public async Task RecoverPendingGatesAsync_BlockedRun_StaysBlocked()
    {
        var factory = CreateFactoryStub();
        var logger = Substitute.For<ILogger<PipelineRunnerService>>();
        var telemetry = Substitute.For<IPipelineTelemetry>();
        var gateStore = CreateGateStoreStub();
        var runStore = CreateRunStoreStub();

        var runId = Guid.NewGuid();
        var gateId = Guid.NewGuid();
        var gate = new StageGate { RunId = runId, GateId = gateId, Status = GateStatus.Pending };
        gateStore.GetAllPendingAsync().Returns(Task.FromResult(new List<StageGate> { gate }));
        gateStore.GetPendingForRunAsync(runId).Returns(Task.FromResult(new List<StageGate> { gate }));
        runStore.GetAllIncompleteAsync().Returns(Task.FromResult(new List<RunCheckpoint>
        {
            new RunCheckpoint(runId, "Requirements", "Running", DateTimeOffset.UtcNow, "")
        }));

        var runner = new PipelineRunnerService(factory, logger, telemetry, gateStore, runStore);

        await runner.RecoverPendingGatesAsync();

        // Run should be active because of pending gate
        runner.ActiveRunCount.Should().Be(1);
    }
}
