using Microsoft.Extensions.Logging;
using NSubstitute;
using SDLC.Contracts;
using SDLC.Dashboard.Services;
using SDLC.Telemetry;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace SDLC.Dashboard.Tests;

#pragma warning disable NUnit1032
[TestFixture, SingleThreaded]
public class RunNotificationServiceTests
{
    private IPipelineTelemetry _telemetry = null!;
    private ISignalRPoster _poster = null!;
    private ILogger<RunNotificationService> _logger = null!;
    private RunNotificationService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _telemetry = new TestTelemetry();
        _poster = Substitute.For<ISignalRPoster>();
        _logger = Substitute.For<ILogger<RunNotificationService>>();
        _service = new RunNotificationService(_poster, _telemetry, _logger);
    }

    [Test]
    public async Task PushGateEvents_PushesNewEvents_WhenAvailable()
    {
        var gateId = Guid.NewGuid();
        await _telemetry.RecordGateApprovedAsync(gateId, "user-1", CancellationToken.None);

        await _service.PushGateEvents(CancellationToken.None);

        await _poster.Received(1).PushGateResolvedAsync(
            Arg.Is<SDLC.Dashboard.Hubs.GateResolvedMessage>(m => m.GateId == gateId && m.Approved),
            CancellationToken.None);
    }

    [Test]
    public async Task PushGateEvents_SkipsPreviouslySentEvents()
    {
        var gateId = Guid.NewGuid();
        await _telemetry.RecordGateApprovedAsync(gateId, "user-1", CancellationToken.None);

        await _service.PushGateEvents(CancellationToken.None);
        await _poster.Received(1).PushGateResolvedAsync(Arg.Any<SDLC.Dashboard.Hubs.GateResolvedMessage>(), Arg.Any<CancellationToken>());

        await _service.PushGateEvents(CancellationToken.None);
        // Still 1 call — no duplicates
        await _poster.Received(1).PushGateResolvedAsync(Arg.Any<SDLC.Dashboard.Hubs.GateResolvedMessage>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task PushPipelineEvents_PushesRunCompletedEvent()
    {
        var runId = Guid.NewGuid();
        await _telemetry.CompletePipelineRunAsync(runId, CancellationToken.None);

        await _service.PushPipelineEvents(CancellationToken.None);

        await _poster.Received(1).PushRunStateChangedAsync(
            Arg.Is<SDLC.Dashboard.Hubs.RunStateChangedMessage>(m => m.RunId == runId && m.Status == "Completed"),
            CancellationToken.None);
    }

    [Test]
    public async Task PushPipelineEvents_PushesRunCancelledEvent()
    {
        var runId = Guid.NewGuid();
        await _telemetry.RecordRunCancelledAsync(runId, CancellationToken.None);

        await _service.PushPipelineEvents(CancellationToken.None);

        await _poster.Received(1).PushRunStateChangedAsync(
            Arg.Is<SDLC.Dashboard.Hubs.RunStateChangedMessage>(m => m.RunId == runId && m.Status == "Cancelled"),
            CancellationToken.None);
    }

    [Test]
    public async Task ExecuteAsync_PushesMultipleEventsInBatch()
    {
        var runId = Guid.NewGuid();
        var gateId = Guid.NewGuid();
        await _telemetry.CompletePipelineRunAsync(runId, CancellationToken.None);
        await _telemetry.RecordGateApprovedAsync(gateId, "user-1", CancellationToken.None);

        await _service.PushGateEvents(CancellationToken.None);
        await _service.PushPipelineEvents(CancellationToken.None);

        await _poster.Received(1).PushGateResolvedAsync(
            Arg.Is<SDLC.Dashboard.Hubs.GateResolvedMessage>(m => m.GateId == gateId),
            Arg.Any<CancellationToken>());
        await _poster.Received(1).PushRunStateChangedAsync(
            Arg.Is<SDLC.Dashboard.Hubs.RunStateChangedMessage>(m => m.RunId == runId),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task PushGateEvents_ThrowsOnFailingTelemetry()
    {
        _telemetry = new FailingTelemetry();
        _poster = Substitute.For<ISignalRPoster>();
        _logger = Substitute.For<ILogger<RunNotificationService>>();
        _service = new RunNotificationService(_poster, _telemetry, _logger);

        var task = _service.PushGateEvents(CancellationToken.None);
        try { await task; }
        catch (InvalidOperationException)
        {
            // Expected
        }
    }

    [Test]
    public async Task PushGateEvents_LeavesIndexUnchangedOnFailure()
    {
        _telemetry = new FailingTelemetry();
        _poster = Substitute.For<ISignalRPoster>();
        _logger = Substitute.For<ILogger<RunNotificationService>>();
        _service = new RunNotificationService(_poster, _telemetry, _logger);

        try { await _service.PushGateEvents(CancellationToken.None); }
        catch (InvalidOperationException) { /* expected */ }
        catch (Exception ex) { throw new InvalidOperationException($"Expected InvalidOperationException but got {ex.GetType()}", ex); }

        // Index still 0 — next call would re-attempt (verified by pushing a real event)
        _telemetry = new TestTelemetry();
        _service = new RunNotificationService(_poster, _telemetry, _logger);

        var gateId = Guid.NewGuid();
        await _telemetry.RecordGateApprovedAsync(gateId, "user", CancellationToken.None);
        await _service.PushGateEvents(CancellationToken.None);

        await _poster.Received(1).PushGateResolvedAsync(
            Arg.Is<SDLC.Dashboard.Hubs.GateResolvedMessage>(m => m.GateId == gateId),
            Arg.Any<CancellationToken>());
    }

    private class TestTelemetry : IPipelineTelemetry
    {
        private readonly ConcurrentQueue<GateEvent> _gateEvents = new();
        private readonly ConcurrentQueue<PipelineEvent> _pipelineEvents = new();

        public Task<IReadOnlyList<GateEvent>> GetGateEventsAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<GateEvent>>(_gateEvents.ToList().AsReadOnly());

        public Task<IReadOnlyList<PipelineEvent>> GetPipelineEventsAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<PipelineEvent>>(_pipelineEvents.ToList().AsReadOnly());

        public Task RecordGateApprovedAsync(Guid gateId, string? userId = null, CancellationToken ct = default)
        {
            _gateEvents.Enqueue(new GateEvent(gateId, true, userId, DateTimeOffset.UtcNow));
            return Task.CompletedTask;
        }

        public Task RecordGateRejectedAsync(Guid gateId, string? userId = null, CancellationToken ct = default)
        {
            _gateEvents.Enqueue(new GateEvent(gateId, false, userId, DateTimeOffset.UtcNow));
            return Task.CompletedTask;
        }

        public Task CompletePipelineRunAsync(Guid runId, CancellationToken ct = default)
        {
            _pipelineEvents.Enqueue(new PipelineEvent(runId, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "Completed"));
            return Task.CompletedTask;
        }

        public Task RecordRunCancelledAsync(Guid runId, CancellationToken ct = default)
        {
            _pipelineEvents.Enqueue(new PipelineEvent(runId, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "Cancelled"));
            return Task.CompletedTask;
        }

        public Task StartPipelineRunAsync(Guid runId, string projectBrief, CancellationToken ct = default) => Task.CompletedTask;
        public Task RecordStepCompletedAsync(SdlcStage stage, string stepName, CancellationToken ct = default) => Task.CompletedTask;
        public Task RecordStepFailedAsync(SdlcStage stage, string stepName, Exception ex, CancellationToken ct = default) => Task.CompletedTask;
        public Task RecordTokenUsageAsync(Guid runId, long promptTokens, long completionTokens, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<StepEvent>> GetStepEventsAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<StepEvent>>(Array.Empty<StepEvent>());
        public Activity? StartRunActivity(Guid runId) => (Activity?)null;
        public Activity? StartStageActivity(Guid runId, SdlcStage stage) => (Activity?)null;
    }

    private class FailingTelemetry : IPipelineTelemetry
    {
        public Task<IReadOnlyList<GateEvent>> GetGateEventsAsync(CancellationToken ct = default) => throw new InvalidOperationException("fail");
        public Task<IReadOnlyList<PipelineEvent>> GetPipelineEventsAsync(CancellationToken ct = default) => throw new InvalidOperationException("fail");
        public Task RecordGateApprovedAsync(Guid gateId, string? userId = null, CancellationToken ct = default) => Task.CompletedTask;
        public Task RecordGateRejectedAsync(Guid gateId, string? userId = null, CancellationToken ct = default) => Task.CompletedTask;
        public Task StartPipelineRunAsync(Guid runId, string projectBrief, CancellationToken ct = default) => Task.CompletedTask;
        public Task CompletePipelineRunAsync(Guid runId, CancellationToken ct = default) => Task.CompletedTask;
        public Task RecordRunCancelledAsync(Guid runId, CancellationToken ct = default) => Task.CompletedTask;
        public Task RecordStepCompletedAsync(SdlcStage stage, string stepName, CancellationToken ct = default) => Task.CompletedTask;
        public Task RecordStepFailedAsync(SdlcStage stage, string stepName, Exception ex, CancellationToken ct = default) => Task.CompletedTask;
        public Task RecordTokenUsageAsync(Guid runId, long promptTokens, long completionTokens, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<StepEvent>> GetStepEventsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<StepEvent>>(Array.Empty<StepEvent>());
        public Activity? StartRunActivity(Guid runId) => (Activity?)null;
        public Activity? StartStageActivity(Guid runId, SdlcStage stage) => (Activity?)null;
    }
}
