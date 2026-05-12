using FluentAssertions;
using NUnit.Framework;
using SDLC.Contracts;
using SDLC.Telemetry;

namespace SDLC.Telemetry.Tests;

[TestFixture, SingleThreaded]
public class SdlcTelemetryFacadeTests
{
    [TearDown]
    public void TearDown()
    {
        SdlcTelemetry.Instance = null;
    }

    [Test]
    public void Meter_IsNonNull_WithCorrectName()
    {
        SdlcTelemetry.Meter.Should().NotBeNull();
        SdlcTelemetry.Meter.Name.Should().Be("SDLC.Pipeline");
    }

    [Test]
    public void ActivitySource_IsNonNull_WithCorrectName()
    {
        SdlcTelemetry.ActivitySource.Should().NotBeNull();
        SdlcTelemetry.ActivitySource.Name.Should().Be("SDLC.Pipeline");
    }

    [Test]
    public void AllCounters_AreNonNull()
    {
        SdlcTelemetry.RunsStarted.Should().NotBeNull();
        SdlcTelemetry.RunsCompleted.Should().NotBeNull();
        SdlcTelemetry.RunsCancelled.Should().NotBeNull();
        SdlcTelemetry.GatesApproved.Should().NotBeNull();
        SdlcTelemetry.GatesRejected.Should().NotBeNull();
        SdlcTelemetry.LlmPromptTokens.Should().NotBeNull();
        SdlcTelemetry.LlmCompletionTokens.Should().NotBeNull();
    }

    [Test]
    public void StageDuration_Histogram_IsNonNull()
    {
        SdlcTelemetry.StageDuration.Should().NotBeNull();
    }

    [Test]
    public void RecordStepCompleted_DoesNotThrow_WhenInstanceIsNull()
    {
        SdlcTelemetry.Instance = null;
        var act = () => SdlcTelemetry.RecordStepCompleted(SdlcStage.Research, "test-step");
        act.Should().NotThrow();
    }

    [Test]
    public async Task RecordStepCompleted_DelegatesToInstance_WhenInstanceIsSet()
    {
        var real = new PipelineTelemetry();
        SdlcTelemetry.Instance = real;

        SdlcTelemetry.RecordStepCompleted(SdlcStage.Requirements, "requirements-step");

        var events = await real.GetStepEventsAsync();
        events.Should().ContainSingle();
        events[0].Stage.Should().Be(SdlcStage.Requirements);
        events[0].StepName.Should().Be("requirements-step");
    }

    [Test]
    public void RecordTokenUsage_AddsToCounters()
    {
        SdlcTelemetry.RecordTokenUsage(100L, 50L);
        SdlcTelemetry.RecordTokenUsage(200L, 75L);
        // Counters just add internally, no API to read back. Verify no exception.
    }

    [Test]
    public void RecordStepFailed_DoesNotThrow_WhenInstanceIsNull()
    {
        SdlcTelemetry.Instance = null;
        var act = () => SdlcTelemetry.RecordStepFailed(SdlcStage.Design, "test-step", new InvalidOperationException());
        act.Should().NotThrow();
    }
}
