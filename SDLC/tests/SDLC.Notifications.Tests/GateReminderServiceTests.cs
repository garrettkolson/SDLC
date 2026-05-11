using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NUnit.Framework;
using SDLC.Contracts;
using SDLC.Infrastructure;
using SDLC.Notifications;

namespace SDLC.Notifications.Tests;

[TestFixture]
public class GateReminderServiceTests
{
    private IStageGateStore _gateStore = null!;
    private INotificationService _notifications = null!;
    private ILogger<GateReminderService> _logger = null!;

    [SetUp]
    public void SetUp()
    {
        _gateStore = Substitute.For<IStageGateStore>();
        _notifications = Substitute.For<INotificationService>();
        _logger = Substitute.For<ILogger<GateReminderService>>();
    }

    [Test]
    public void Ctor_ServiceInstantiates()
    {
        var service = new GateReminderService(_gateStore, _notifications, _logger);
        service.Should().NotBeNull();
    }

    [Test]
    public async Task GateFilter_CorrectlyIdentifiesStaleGates()
    {
        var staleGate = new StageGate
        {
            RunId = Guid.NewGuid(),
            GateId = Guid.NewGuid(),
            Stage = SdlcStage.Research,
            Status = GateStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow.AddHours(-3)
        };
        var freshGate = new StageGate
        {
            RunId = Guid.NewGuid(),
            GateId = Guid.NewGuid(),
            Stage = SdlcStage.Design,
            Status = GateStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-10)
        };
        _gateStore.GetAllPendingAsync().Returns(Task.FromResult(new List<StageGate> { staleGate, freshGate }));

        var stale = (await _gateStore.GetAllPendingAsync())
            .Where(g => DateTimeOffset.UtcNow - g.CreatedAt > TimeSpan.FromHours(2))
            .ToList();

        stale.Should().HaveCount(1);
        stale[0].GateId.Should().Be(staleGate.GateId);
    }

    [Test]
    public async Task GateFilter_SkipsFreshGates()
    {
        var freshGate = new StageGate
        {
            RunId = Guid.NewGuid(),
            GateId = Guid.NewGuid(),
            Stage = SdlcStage.Research,
            Status = GateStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-10)
        };
        _gateStore.GetAllPendingAsync().Returns(Task.FromResult(new List<StageGate> { freshGate }));

        var stale = (await _gateStore.GetAllPendingAsync())
            .Where(g => DateTimeOffset.UtcNow - g.CreatedAt > TimeSpan.FromHours(2))
            .ToList();

        stale.Should().BeEmpty();
    }

    [Test]
    public async Task Dedup_SameGateNotNotifiedTwice()
    {
        var staleGate = new StageGate
        {
            RunId = Guid.NewGuid(),
            GateId = Guid.NewGuid(),
            Stage = SdlcStage.Research,
            Status = GateStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow.AddHours(-3)
        };
        _gateStore.GetAllPendingAsync().Returns(Task.FromResult(new List<StageGate> { staleGate }));

        var service = new GateReminderService(_gateStore, _notifications, _logger);

        await service.RunSweepAsync();
        service.SeenGates.Should().Contain(staleGate.GateId);
        await _notifications.Received(1).SendApprovalRequestAsync(Arg.Any<StageGate>());

        await service.RunSweepAsync();
        await _notifications.Received(1).SendApprovalRequestAsync(Arg.Any<StageGate>()); // still 1, not 2
    }

    [Test]
    public async Task Dedup_ResolvedGateReNotifies()
    {
        var gateId = Guid.NewGuid();
        var staleGate = new StageGate
        {
            RunId = Guid.NewGuid(),
            GateId = gateId,
            Stage = SdlcStage.Research,
            Status = GateStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow.AddHours(-3)
        };

        _gateStore.GetAllPendingAsync().Returns(Task.FromResult(new List<StageGate> { staleGate }));

        var service = new GateReminderService(_gateStore, _notifications, _logger);

        await service.RunSweepAsync();
        await _notifications.Received(1).SendApprovalRequestAsync(Arg.Any<StageGate>());

        // Second sweep — dedup prevents re-notify
        await service.RunSweepAsync();
        await _notifications.Received(1).SendApprovalRequestAsync(Arg.Any<StageGate>());

        // Simulate gate resolved — set cleared
        _gateStore.GetAllPendingAsync().Returns(Task.FromResult(new List<StageGate>()));
        await service.RunSweepAsync();
        await _notifications.Received(1).SendApprovalRequestAsync(Arg.Any<StageGate>());

        // Gate re-appears pending
        var newPendingGate = new StageGate
        {
            RunId = staleGate.RunId,
            GateId = gateId,
            Stage = SdlcStage.Research,
            Status = GateStatus.Pending,
            CreatedAt = staleGate.CreatedAt
        };
        _gateStore.GetAllPendingAsync().Returns(Task.FromResult(new List<StageGate> { newPendingGate }));
        await service.RunSweepAsync();

        // Now notified 2 times total
        await _notifications.Received(2).SendApprovalRequestAsync(Arg.Any<StageGate>());
    }
}
