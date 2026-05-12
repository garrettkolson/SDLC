using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NUnit.Framework;
using SDLC.Dashboard.Controllers;
using SDLC.Contracts;
using SDLC.Telemetry;

namespace SDLC.Dashboard.Tests;

[TestFixture]
public class TelemetryControllerTests
{
    private IPipelineTelemetry _telemetry = null!;

    [SetUp]
    public void SetUp()
    {
        _telemetry = Substitute.For<IPipelineTelemetry>();
    }

    [Test]
    public async Task StepsAsync_ReturnsSuccess()
    {
        var expected = new List<StepEvent> { new StepEvent(SdlcStage.Research, "ResearchBrief", true, DateTimeOffset.UtcNow) };
        _telemetry.GetStepEventsAsync().Returns(expected);

        var controller = new TelemetryController(_telemetry);
        var result = await controller.StepsAsync();

        result.Should().BeEquivalentTo(expected);
    }

    [Test]
    public async Task GatesAsync_ReturnsSuccess()
    {
        var expected = new List<GateEvent> { new GateEvent(Guid.NewGuid(), true, "user-123", DateTimeOffset.UtcNow) };
        _telemetry.GetGateEventsAsync().Returns(expected);

        var controller = new TelemetryController(_telemetry);
        var result = await controller.GatesAsync();

        result.Should().BeEquivalentTo(expected);
    }

    [Test]
    public async Task PipelinesAsync_ReturnsSuccess()
    {
        var expected = new List<PipelineEvent> { new PipelineEvent(Guid.NewGuid(), "hash", DateTimeOffset.UtcNow, null) };
        _telemetry.GetPipelineEventsAsync().Returns(expected);

        var controller = new TelemetryController(_telemetry);
        var result = await controller.PipelinesAsync();

        result.Should().BeEquivalentTo(expected);
    }
}
