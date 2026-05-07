using System.Collections.Concurrent;
using System.Diagnostics;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NUnit.Framework;
using SDLC.Contracts;
using SDLC.Infrastructure;
using SDLC.Notifications;
using SDLC.Orchestrator;
using SDLC.Telemetry;

namespace SDLC.Integration.Tests;

/// <summary>
/// Pipeline cancellation integration tests.
/// Verifies that CancelRunAsync cancels the CTS, the token propagates through the pipeline,
/// and the pipeline task completes rather than hangs.
/// </summary>
[TestFixture]
public class PipelineCancellationTests
{
    private string _dbPath = null!;
    private string _tempDir = null!;

    [SetUp]
    public async Task SetUp()
    {
        _dbPath = Path.GetTempFileName();
        _tempDir = Path.Combine(Path.GetTempPath(), $"sdlc-cancel-{Guid.NewGuid():N}");
        var concreteArtifactStore = new ArtifactStore($"Data Source={_dbPath}", _tempDir);
        var concreteGateStore = new StageGateStore($"Data Source={_dbPath}");
        await concreteArtifactStore.InitializeAsync();
        await concreteGateStore.InitializeAsync();
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

    private static ConcurrentDictionary<Guid, CancellationTokenSource> GetRunCancellationCts(PipelineRunnerService runner)
    {
        var field = typeof(PipelineRunnerService).GetField(
            "_runCancellation", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        return (ConcurrentDictionary<Guid, CancellationTokenSource>)field!.GetValue(runner)!;
    }

    /// <summary>
    /// Full pipeline cancellation: CTS is created, run starts with a long-running factory task
    /// that respects cancellation, CancelRunAsync fires, task completes within timeout.
    /// Key property: the task completes rather than hanging forever.
    /// </summary>
    [Test]
    public async Task CancelRunAsync_CancelsRunningPipeline()
    {
        var runId = Guid.NewGuid();
        var config = new SdlcRunConfig { RunId = runId, ProjectBrief = "Cancellation test" };

        var processFactory = Substitute.For<ISdlcProcessFactory>();
        Task? capturedPipelineTask = null;
        processFactory.StartAsync(Arg.Any<SdlcRunConfig>(), Arg.Any<CancellationToken>())
            .Returns(x =>
            {
                var ct = x.ArgAt<CancellationToken>(1);
                capturedPipelineTask = Task.Delay(5000, ct);
                return new ProcessHandle(capturedPipelineTask);
            });

        var logger = Substitute.For<ILogger<PipelineRunnerService>>();
        var telemetry = Substitute.For<IPipelineTelemetry>();
        telemetry.StartRunActivity(Arg.Any<Guid>()).Returns((Activity?)null);
        var runner = new PipelineRunnerService(processFactory, logger, telemetry,
            Substitute.For<IStageGateStore>(), Substitute.For<IRunStore>());

        await runner.EnqueueAsync(config);
        runner.IsRunActive(runId).Should().BeTrue("pipeline should be tracking the run");

        // Wait for the task to start
        await Task.Delay(100);
        capturedPipelineTask.Should().NotBeNull("pipeline task should have been created");

        // Cancel the run
        await runner.CancelRunAsync(runId);

        // Wait for the pipeline task to complete
        var completed = await Task.WhenAny(capturedPipelineTask!, Task.Delay(10000));
        completed.Should().Be(capturedPipelineTask, "pipeline should complete within timeout, not hang");

        // Verify CTS was removed from _runCancellation
        var dict = GetRunCancellationCts(runner);
        dict.ContainsKey(runId).Should().BeFalse("_runCancellation entry should be removed on cancellation");
    }

    /// <summary>
    /// Verify the cancellation token flows from the factory into the pipeline task body.
    /// </summary>
    [Test]
    public async Task CancelRunAsync_PipelineTaskObservesCancellation()
    {
        var runId = Guid.NewGuid();
        var config = new SdlcRunConfig { RunId = runId, ProjectBrief = "Token propagation test" };

        var processFactory = Substitute.For<ISdlcProcessFactory>();
        var ctsCapture = new TaskCompletionSource<CancellationToken>();
        processFactory.StartAsync(Arg.Any<SdlcRunConfig>(), Arg.Any<CancellationToken>())
            .Returns(x =>
            {
                var ct = x.ArgAt<CancellationToken>(1);
                ctsCapture.SetResult(ct);
                return new ProcessHandle(ct.IsCancellationRequested
                    ? Task.FromCanceled(ct)
                    : Task.Delay(Timeout.Infinite, ct));
            });

        var logger = Substitute.For<ILogger<PipelineRunnerService>>();
        var telemetry = Substitute.For<IPipelineTelemetry>();
        telemetry.StartRunActivity(Arg.Any<Guid>()).Returns((Activity?)null);
        var runner = new PipelineRunnerService(processFactory, logger, telemetry,
            Substitute.For<IStageGateStore>(), Substitute.For<IRunStore>());

        await runner.EnqueueAsync(config);
        var receivedCt = await ctsCapture.Task;
        receivedCt.IsCancellationRequested.Should().BeFalse("factory should receive a non-cancelled token initially");

        await runner.CancelRunAsync(runId);
        receivedCt.IsCancellationRequested.Should().BeTrue("factory token should be cancelled after CancelRunAsync");

        // Verify factory was called with the right token
        processFactory.Received(1).StartAsync(Arg.Any<SdlcRunConfig>(), receivedCt);
    }

    /// <summary>
    /// Verify _runCancellation is created before CancelRunAsync is called.
    /// </summary>
    [Test]
    public async Task CancelRunAsync_CTSExistsBeforeCancellation()
    {
        var runId = Guid.NewGuid();
        var config = new SdlcRunConfig { RunId = runId, ProjectBrief = "CTS exists test" };

        var processFactory = Substitute.For<ISdlcProcessFactory>();
        processFactory.StartAsync(Arg.Any<SdlcRunConfig>(), Arg.Any<CancellationToken>())
            .Returns(new ProcessHandle(new TaskCompletionSource<object?>().Task));

        var logger = Substitute.For<ILogger<PipelineRunnerService>>();
        var telemetry = Substitute.For<IPipelineTelemetry>();
        telemetry.StartRunActivity(Arg.Any<Guid>()).Returns((Activity?)null);
        var runner = new PipelineRunnerService(processFactory, logger, telemetry,
            Substitute.For<IStageGateStore>(), Substitute.For<IRunStore>());

        await runner.EnqueueAsync(config);

        var dict = GetRunCancellationCts(runner);
        dict.ContainsKey(runId).Should().BeTrue("_runCancellation entry should exist after EnqueueAsync");

        // The CTS should not be cancelled yet
        var cts = dict[runId];
        cts.IsCancellationRequested.Should().BeFalse("CTS should not be cancelled before CancelRunAsync");
    }

    /// <summary>
    /// Multiple cancellation calls on same runId are safe — only first one should remove the CTS.
    /// </summary>
    [Test]
    public async Task CancelRunAsync_DoubleCancel_IsIdempotent()
    {
        var runId = Guid.NewGuid();
        var config = new SdlcRunConfig { RunId = runId, ProjectBrief = "Idempotent test" };

        var processFactory = Substitute.For<ISdlcProcessFactory>();
        processFactory.StartAsync(Arg.Any<SdlcRunConfig>(), Arg.Any<CancellationToken>())
            .Returns(new ProcessHandle(new TaskCompletionSource<object?>().Task));

        var logger = Substitute.For<ILogger<PipelineRunnerService>>();
        var telemetry = Substitute.For<IPipelineTelemetry>();
        telemetry.StartRunActivity(Arg.Any<Guid>()).Returns((Activity?)null);
        var runner = new PipelineRunnerService(processFactory, logger, telemetry,
            Substitute.For<IStageGateStore>(), Substitute.For<IRunStore>());

        await runner.EnqueueAsync(config);

        // First cancel — should work
        await runner.CancelRunAsync(runId);

        // Second cancel — should not throw (CTS already removed)
        await runner.CancelRunAsync(runId);

        // IsRunActive still true (continuation hasn't fired)
        runner.IsRunActive(runId).Should().BeTrue();

        // CTS should be gone
        var dict = GetRunCancellationCts(runner);
        dict.ContainsKey(runId).Should().BeFalse();
    }
}
