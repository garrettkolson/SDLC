using FluentAssertions;
using NUnit.Framework;
using SDLC.Contracts;
using SDLC.Telemetry;

namespace SDLC.Telemetry.Tests;

[TestFixture, SingleThreaded]
public class PipelineTelemetryTests
{
    private PipelineTelemetry _telemetry = null!;

    [SetUp]
    public void SetUp()
    {
        _telemetry = new PipelineTelemetry();
    }

    [Test]
    public async Task RecordStepCompletedAsync_RecordsEvent()
    {
        await _telemetry.RecordStepCompletedAsync(SdlcStage.Research, "ResearchBrief");
        var events = await _telemetry.GetStepEventsAsync();

        events.Should().ContainSingle();
        events[0].Stage.Should().Be(SdlcStage.Research);
        events[0].StepName.Should().Be("ResearchBrief");
        events[0].Succeeded.Should().BeTrue();
        events[0].Error.Should().BeNull();
    }

    [Test]
    public async Task RecordStepFailedAsync_RecordsEventWithError()
    {
        var ex = new InvalidOperationException("Model timed out");
        await _telemetry.RecordStepFailedAsync(SdlcStage.Design, "Architecture", ex);
        var events = await _telemetry.GetStepEventsAsync();

        events.Should().ContainSingle();
        events[0].Succeeded.Should().BeFalse();
        events[0].Error.Should().Be("Model timed out");
        events[0].Stage.Should().Be(SdlcStage.Design);
    }

    [Test]
    public async Task MultipleStepEvents_PreservesOrder()
    {
        await _telemetry.RecordStepCompletedAsync(SdlcStage.Research, "Research");
        await _telemetry.RecordStepCompletedAsync(SdlcStage.Requirements, "Requirements");
        await _telemetry.RecordStepCompletedAsync(SdlcStage.Design, "Design");

        var events = await _telemetry.GetStepEventsAsync();
        events.Should().HaveCount(3);
        events[0].StepName.Should().Be("Research");
        events[1].StepName.Should().Be("Requirements");
        events[2].StepName.Should().Be("Design");
    }

    [Test]
    public async Task RecordGateApprovedAsync_RecordsEvent()
    {
        var gateId = Guid.NewGuid();
        await _telemetry.RecordGateApprovedAsync(gateId, "user-456");
        var events = await _telemetry.GetGateEventsAsync();

        events.Should().ContainSingle();
        events[0].GateId.Should().Be(gateId);
        events[0].Approved.Should().BeTrue();
        events[0].UserId.Should().Be("user-456");
    }

    [Test]
    public async Task RecordGateRejectedAsync_RecordsEvent()
    {
        var gateId = Guid.NewGuid();
        await _telemetry.RecordGateRejectedAsync(gateId, "user-789");
        var events = await _telemetry.GetGateEventsAsync();

        events.Should().ContainSingle();
        events[0].GateId.Should().Be(gateId);
        events[0].Approved.Should().BeFalse();
        events[0].UserId.Should().Be("user-789");
    }

    [Test]
    public async Task StartPipelineRunAsync_RecordsEvent()
    {
        var runId = Guid.NewGuid();
        var brief = "Build an invoice system";
        await _telemetry.StartPipelineRunAsync(runId, brief);

        var events = await _telemetry.GetPipelineEventsAsync();

        events.Should().ContainSingle();
        events[0].RunId.Should().Be(runId);
        events[0].ProjectBriefHash.Should().Be(PipelineTelemetry.HashProjectBrief(brief));
        events[0].Ended.Should().BeNull();
    }

    [Test]
    public async Task CompletePipelineRunAsync_SetsEndTimestamp()
    {
        var runId = Guid.NewGuid();
        await _telemetry.StartPipelineRunAsync(runId, "brief");
        await _telemetry.CompletePipelineRunAsync(runId);

        var events = await _telemetry.GetPipelineEventsAsync();

        var startEvent = events[0];
        var endEvent = events[1];

        startEvent.RunId.Should().Be(runId);
        startEvent.Ended.Should().BeNull();
        endEvent.RunId.Should().Be(runId);
        endEvent.Ended.Should().NotBeNull();
        endEvent.Ended!.Value.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(2));
    }

    [Test]
    public async Task Events_IncludeTimestamps()
    {
        await _telemetry.RecordStepCompletedAsync(SdlcStage.Research, "test");
        var events = await _telemetry.GetStepEventsAsync();

        events[0].Timestamp.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(2));
    }
}
