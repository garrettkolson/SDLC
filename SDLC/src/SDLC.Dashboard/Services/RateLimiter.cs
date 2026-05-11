using Microsoft.AspNetCore.Http;
using System.Collections.Concurrent;

namespace SDLC.Dashboard.Services;

public class RateLimiter
{
    private readonly int _permitLimit;
    private readonly TimeSpan _window;
    private readonly ConcurrentDictionary<string, WindowState> _windows = new();

    public RateLimiter(int permitLimit, TimeSpan window)
    {
        _permitLimit = permitLimit;
        _window = window;
    }

    public bool Allow(string key)
    {
        var now = DateTimeOffset.UtcNow;
        var state = _windows.GetOrAdd(key, _ => new WindowState { Start = now, Count = 0 });

        lock (state)
        {
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

    public void Sweep()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var kvp in _windows)
        {
            if ((now - kvp.Value.Start) > _window)
            {
                _windows.TryRemove(kvp.Key, out _);
            }
        }
    }

    private class WindowState
    {
        public DateTimeOffset Start;
        public int Count;
    }
}
