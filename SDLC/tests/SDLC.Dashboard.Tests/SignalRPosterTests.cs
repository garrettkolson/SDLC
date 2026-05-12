using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using NUnit.Framework;
using SDLC.Dashboard.Hubs;
using SDLC.Dashboard.Services;

namespace SDLC.Dashboard.Tests;

[TestFixture, SingleThreaded]
public class SignalRPosterTests
{
    private TrackingHubContext _hubContext = null!;
    private SignalRPoster _poster = null!;

    [SetUp]
    public void SetUp()
    {
        _hubContext = new TrackingHubContext();
        _poster = new SignalRPoster(_hubContext);
    }

    [Test]
    public async Task PushGateResolvedAsync_CallsAllAndGroup()
    {
        var msg = new GateResolvedMessage(Guid.NewGuid(), true, null);
        await _poster.PushGateResolvedAsync(msg);

        _hubContext.AllEvents.Should().ContainSingle(e => e == "GateResolved");
        _hubContext.GroupEvents.Should().ContainSingle(e => e == "GateResolved");
    }

    [Test]
    public async Task PushRunStateChangedAsync_CallsAllAndGroup()
    {
        var msg = new RunStateChangedMessage(Guid.NewGuid(), "Running");
        await _poster.PushRunStateChangedAsync(msg);

        _hubContext.AllEvents.Should().ContainSingle(e => e == "RunStateChanged");
        _hubContext.GroupEvents.Should().ContainSingle(e => e == "RunStateChanged");
    }

    [Test]
    public async Task PushGateResolvedAsync_Twice_EachEndpointCalledTwice()
    {
        var msg1 = new GateResolvedMessage(Guid.NewGuid(), true, null);
        var msg2 = new GateResolvedMessage(Guid.NewGuid(), true, null);
        await _poster.PushGateResolvedAsync(msg1);
        await _poster.PushGateResolvedAsync(msg2);

        _hubContext.AllEvents.Count(e => e == "GateResolved").Should().Be(2);
        _hubContext.GroupEvents.Count(e => e == "GateResolved").Should().Be(2);
    }

    private class TrackingHubContext : IHubContext<RunStateHub>
    {
        public List<string> AllEvents { get; } = new();
        public List<string> GroupEvents { get; } = new();

        public IHubProtocolResolver ProtocolResolver => null!;
        public IGroupManager Groups => null!;

        public IHubClients Clients => new TrackingClients(this);

        private class TrackingClients(TrackingHubContext ctx) : IHubClients
        {
            public IClientProxy All => new TrackingClientProxy(ctx.AllEvents);
            public IClientProxy AllExcept(IReadOnlyList<string> userIds) => new TrackingClientProxy(ctx.AllEvents);
            public IClientProxy Client(string connectionId) => new TrackingClientProxy(ctx.AllEvents);
            public IClientProxy Group(string groupName) => groupName == "runs"
                ? new TrackingClientProxy(ctx.GroupEvents)
                : new TrackingClientProxy(ctx.AllEvents);
            public IClientProxy Groups(IReadOnlyList<string> groupNames) => new TrackingClientProxy(ctx.AllEvents);
            public IClientProxy User(string userId) => new TrackingClientProxy(ctx.AllEvents);
            public IClientProxy Users(IReadOnlyList<string> userIds) => new TrackingClientProxy(ctx.AllEvents);
            public IClientProxy Clients(IReadOnlyList<string> userIds) => new TrackingClientProxy(ctx.AllEvents);
            public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> userIds) => new TrackingClientProxy(ctx.GroupEvents);
        }

        private class TrackingClientProxy(List<string> store) : IClientProxy
        {
            public Task SendCoreAsync(string methodName, object?[]? args, CancellationToken cancellationToken = default)
            {
                store.Add(methodName);
                return Task.CompletedTask;
            }

            public Task SendAsync(string methodName, object? arg, CancellationToken cancellationToken = default)
            {
                store.Add(methodName);
                return Task.CompletedTask;
            }

            public Task SendAsync(string methodName, object? arg1, object? arg2, CancellationToken cancellationToken = default)
            {
                store.Add(methodName);
                return Task.CompletedTask;
            }

            public Task SendAsync(string methodName, object? arg1, object? arg2, object? arg3, CancellationToken cancellationToken = default)
            {
                store.Add(methodName);
                return Task.CompletedTask;
            }
        }
    }
}
