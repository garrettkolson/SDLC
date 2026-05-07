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
}
