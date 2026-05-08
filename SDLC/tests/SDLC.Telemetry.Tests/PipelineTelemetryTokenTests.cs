using FluentAssertions;
using NUnit.Framework;
using SDLC.Telemetry;

namespace SDLC.Telemetry.Tests;

[TestFixture, SingleThreaded]
public class PipelineTelemetryTokenTests
{
    private PipelineTelemetry _telemetry = null!;

    [SetUp]
    public void SetUp()
    {
        _telemetry = new PipelineTelemetry();
    }

    [Test]
    public async Task RecordTokenUsageAsync_CallsStaticCounter()
    {
        var runId = Guid.NewGuid();
        await _telemetry.RecordTokenUsageAsync(runId, 100, 200);
        // Method completes without crash — static counter accepts the values
    }

    [Test]
    public async Task RecordTokenUsageAsync_HandlesMultipleCalls()
    {
        var runId = Guid.NewGuid();
        await _telemetry.RecordTokenUsageAsync(runId, 100, 50);
        await _telemetry.RecordTokenUsageAsync(runId, 200, 75);
        // No crash on multiple calls
    }
}
