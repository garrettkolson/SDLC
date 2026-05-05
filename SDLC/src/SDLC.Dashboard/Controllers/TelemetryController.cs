using Microsoft.AspNetCore.Mvc;
using SDLC.Telemetry;

namespace SDLC.Dashboard.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TelemetryController(IPipelineTelemetry telemetry) : ControllerBase
{
    [HttpGet("steps")]
    public Task<IReadOnlyList<StepEvent>> StepsAsync() => telemetry.GetStepEventsAsync();

    [HttpGet("gates")]
    public Task<IReadOnlyList<GateEvent>> GatesAsync() => telemetry.GetGateEventsAsync();

    [HttpGet("pipelines")]
    public Task<IReadOnlyList<PipelineEvent>> PipelinesAsync() => telemetry.GetPipelineEventsAsync();
}
