using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace SDLC.Telemetry;

public static class SdlcTelemetry
{
    public static readonly ActivitySource ActivitySource = new("SDLC.Pipeline");
    public static readonly Meter Meter = new("SDLC.Pipeline");

    public static readonly Counter<long> RunsStarted = Meter.CreateCounter<long>("sdlc.runs_started");
    public static readonly Counter<long> RunsCompleted = Meter.CreateCounter<long>("sdlc.runs_completed");
    public static readonly Counter<long> GatesApproved = Meter.CreateCounter<long>("sdlc.gates_approved");
    public static readonly Counter<long> GatesRejected = Meter.CreateCounter<long>("sdlc.gates_rejected");
    public static readonly Histogram<double> StageDuration = Meter.CreateHistogram<double>("sdlc.stage_duration_ms");
}
