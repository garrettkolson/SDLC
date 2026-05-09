using Serilog.Context;

namespace SDLC.Orchestrator.Logging;

/// <summary>
/// Pushes structured log scope properties via Serilog's LogContext.
/// Properties flow through Enrich.FromLogContext() and appear in every log event.
/// </summary>
public static class LogScope
{
    public static IDisposable ForRun(Guid runId)
        => LogContext.PushProperty("RunId", runId);

    public static IDisposable ForGate(Guid gateId)
        => LogContext.PushProperty("GateId", gateId);

    public static IDisposable ForStage(string stage)
        => LogContext.PushProperty("Stage", stage);
}
