using System.Diagnostics;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NUnit.Framework;
using SDLC.Contracts;
using SDLC.Infrastructure;
using SDLC.Notifications;
using SDLC.Orchestrator.Logging;
using SDLC.Telemetry;

namespace SDLC.Orchestrator.Tests;

[TestFixture, SingleThreaded]
public class PipelineShutdownServiceTests
{
    private PipelineRunnerService _runner = null!;
    private IRunStore _runStore = null!;
    private SDLC.Orchestrator.PipelineShutdownService _service = null!;
    private readonly System.Reflection.FieldInfo _activeRunsField = typeof(PipelineRunnerService)
        .GetField("_activeRuns", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;

    [SetUp]
    public void SetUp()
    {
        var processFactory = Substitute.For<ISdlcProcessFactory>();
        var logger = Substitute.For<ILogger<PipelineRunnerService>>();
        var telemetry = Substitute.For<IPipelineTelemetry>();
        telemetry.StartRunActivity(Arg.Any<Guid>()).Returns((Activity?)null);
        var gateStore = Substitute.For<IStageGateStore>();
        _runStore = Substitute.For<IRunStore>();
        var budgetTracker = Substitute.For<IRunBudgetTracker>();
        _runner = new PipelineRunnerService(processFactory, logger, telemetry, gateStore, _runStore, budgetTracker);
        _service = new SDLC.Orchestrator.PipelineShutdownService(_runner, _runStore, Substitute.For<ILogger<SDLC.Orchestrator.PipelineShutdownService>>());
    }

    private Guid AddInFlight(Task task)
    {
        var runId = Guid.NewGuid();
        var dict = (System.Collections.Concurrent.ConcurrentDictionary<Guid, Task>)_activeRunsField.GetValue(_runner)!;
        dict[runId] = task;
        return runId;
    }

    [Test]
    public void StartAsync_ReturnsCompletedTask()
    {
        _service.StartAsync(CancellationToken.None).IsCompleted.Should().BeTrue();
    }

    [Test]
    public async Task StopAsync_NoInFlightTasks_DoesNothing()
    {
        var ct = CancellationToken.None;
        var act = () => _service.StopAsync(ct);
        await act.Should().NotThrowAsync();

        await _runStore.DidNotReceive().GetRunAsync(Arg.Any<Guid>());
    }

    [Test]
    public async Task StopAsync_WaitsForInFlightTasks()
    {
        var tcs = new TaskCompletionSource();
        var runId = AddInFlight(tcs.Task);

        // Create a checkpoint for the active run
        var checkpoint = new RunCheckpoint(runId, "Build", "Running", DateTimeOffset.UtcNow, null);
        _runStore.GetRunAsync(runId).Returns(Task.FromResult<RunCheckpoint?>(checkpoint));

        var ct = CancellationToken.None;
        var stopTask = _service.StopAsync(ct);

        await Task.Delay(50);
        tcs.SetResult();

        var act = () => stopTask;
        await act.Should().NotThrowAsync();
    }

    [Test]
    public async Task StopAsync_PersistsFailedStateForActiveRuns()
    {
        var tcs = new TaskCompletionSource();
        var runId = AddInFlight(tcs.Task);

        var checkpoint = new RunCheckpoint(runId, "Build", "Running", DateTimeOffset.UtcNow, null);
        _runStore.GetRunAsync(runId).Returns(Task.FromResult<RunCheckpoint?>(checkpoint));

        var ct = CancellationToken.None;
        var stopTask = _service.StopAsync(ct);

        tcs.SetResult();
        var act = () => stopTask;
        await act.Should().NotThrowAsync();

        await _runStore.Received(1).UpdateStageAsync(runId, "Build", "Failed");
    }

    [Test]
    public async Task StopAsync_NullCheckpoint_SkipsPersist()
    {
        var tcs = new TaskCompletionSource();
        var runId = AddInFlight(tcs.Task);
        _runStore.GetRunAsync(runId).Returns(Task.FromResult<RunCheckpoint?>(null!));

        var ct = CancellationToken.None;
        var stopTask = _service.StopAsync(ct);

        tcs.SetResult();
        var act = () => stopTask;
        await act.Should().NotThrowAsync();

        await _runStore.DidNotReceive().UpdateStageAsync(runId, Arg.Any<string>(), Arg.Any<string>());
    }

    [Test]
    public async Task StopAsync_PersistFailure_DoesNotAbortOthers()
    {
        var tcs = new TaskCompletionSource();
        var runId1 = AddInFlight(tcs.Task);
        var runId2 = Guid.NewGuid();

        // Manually add runId2 to _activeRuns
        var dict = (System.Collections.Concurrent.ConcurrentDictionary<Guid, Task>)_activeRunsField.GetValue(_runner)!;
        dict[runId2] = Task.CompletedTask;

        var cp1 = new RunCheckpoint(runId1, "Research", "Running", DateTimeOffset.UtcNow, null);
        var cp2 = new RunCheckpoint(runId2, "Requirements", "Running", DateTimeOffset.UtcNow, null);
        _runStore.GetRunAsync(runId1).Returns(Task.FromResult<RunCheckpoint?>(cp1));
        _runStore.GetRunAsync(runId2).Returns(Task.FromResult<RunCheckpoint?>(cp2));

        _runStore.UpdateStageAsync(runId1, "Research", "Failed").Returns(Task.CompletedTask);
        _runStore.UpdateStageAsync(runId2, "Requirements", "Failed").Returns(Task.FromException(new InvalidOperationException("store error")));

        var ct = CancellationToken.None;
        var stopTask = _service.StopAsync(ct);

        tcs.SetResult();
        var act = () => stopTask;
        await act.Should().NotThrowAsync();

        await _runStore.Received(1).UpdateStageAsync(runId1, "Research", "Failed");
        await _runStore.Received(1).UpdateStageAsync(runId2, "Requirements", "Failed");
    }
}
