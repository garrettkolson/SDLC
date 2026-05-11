using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SDLC.Dashboard.Hubs;
using SDLC.Telemetry;

namespace SDLC.Dashboard.Services;

public class RunNotificationService : BackgroundService
{
    private readonly ISignalRPoster _poster;
    private readonly IPipelineTelemetry _telemetry;
    private readonly ILogger<RunNotificationService> _logger;
    private int _lastGateIndex = 0;
    private int _lastPipelineIndex = 0;

    public RunNotificationService(
        ISignalRPoster poster,
        IPipelineTelemetry telemetry,
        ILogger<RunNotificationService> logger)
    {
        _poster = poster;
        _telemetry = telemetry;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PushGateEvents(stoppingToken);
                await PushPipelineEvents(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in RunNotificationService");
            }
            await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
        }
    }

    internal async Task PushGateEvents(CancellationToken ct)
    {
        var events = await _telemetry.GetGateEventsAsync(ct);
        for (var i = _lastGateIndex; i < events.Count; i++)
        {
            var evt = events[i];
            var msg = new GateResolvedMessage(evt.GateId, evt.Approved, null);
            await _poster.PushGateResolvedAsync(msg, ct);
        }
        _lastGateIndex = events.Count;
    }

    internal async Task PushPipelineEvents(CancellationToken ct)
    {
        var events = await _telemetry.GetPipelineEventsAsync(ct);
        for (var i = _lastPipelineIndex; i < events.Count; i++)
        {
            var evt = events[i];
            var msg = new RunStateChangedMessage(evt.RunId, evt.Status);
            await _poster.PushRunStateChangedAsync(msg, ct);
        }
        _lastPipelineIndex = events.Count;
    }
}
