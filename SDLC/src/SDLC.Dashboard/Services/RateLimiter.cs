using Microsoft.AspNetCore.Http;
using System.Collections.Concurrent;

namespace SDLC.Dashboard.Services;

/// <summary>
/// Simple fixed-window rate limiter middleware. Prevents abuse of write-heavy endpoints.
/// Keyed by authenticated username or remote IP address.
/// </summary>
public class RateLimiter
{
    private readonly int _permitLimit;
    private readonly TimeSpan _window;
    private readonly ConcurrentDictionary<string, object> _locks = new();
    private readonly ConcurrentDictionary<string, WindowState> _windows = new();

    public RateLimiter(int permitLimit, TimeSpan window)
    {
        _permitLimit = permitLimit;
        _window = window;
    }

    public bool Allow(string key)
    {
        // Per-key lock for thread safety
        var lockObj = _locks.GetOrAdd(key, _ => new object());
        lock (lockObj)
        {
            var now = DateTimeOffset.UtcNow;
            var state = _windows.GetOrAdd(key, _ => new WindowState { Start = now });

            if ((now - state.Start) > _window)
            {
                state.Start = now;
                state.Count = 1;
                return true;
            }

            state.Count++;
            return state.Count <= _permitLimit;
        }
    }

    private class WindowState
    {
        public DateTimeOffset Start;
        public int Count;
    }
}
