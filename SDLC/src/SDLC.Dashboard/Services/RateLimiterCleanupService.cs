using Microsoft.Extensions.Hosting;

namespace SDLC.Dashboard.Services;

internal sealed class RateLimiterCleanupService : BackgroundService
{
    private readonly RateLimiter _rateLimiter;
    private readonly TimeSpan _interval = TimeSpan.FromMinutes(5);

    public RateLimiterCleanupService(RateLimiter rateLimiter)
    {
        _rateLimiter = rateLimiter;
    }

    protected override Task ExecuteAsync(CancellationToken ct)
    {
        return Task.Delay(Timeout.Infinite, ct);
    }

    public override Task StartAsync(CancellationToken ct = default)
    {
        var timer = new PeriodicTimer(_interval);
        _ = Task.Run(async () =>
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                _rateLimiter.Sweep();
            }
        }, ct);
        return Task.CompletedTask;
    }

    public override Task StopAsync(CancellationToken ct = default)
    {
        return Task.CompletedTask;
    }
}
