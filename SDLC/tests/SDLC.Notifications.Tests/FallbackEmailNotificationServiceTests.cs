using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NUnit.Framework;
using SDLC.Contracts;
using SDLC.Infrastructure;
using SDLC.Notifications;

namespace SDLC.Notifications.Tests;

[TestFixture]
public class FallbackEmailNotificationServiceTests
{
    private ILogger<FallbackEmailNotificationService> _logger = null!;
    private FallbackEmailNotificationService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _logger = Substitute.For<ILogger<FallbackEmailNotificationService>>();
        _service = new FallbackEmailNotificationService(_logger);
    }

    [Test]
    public async Task SendApprovalRequestAsync_ReturnsCompletedTask()
    {
        var gate = new StageGate { GateId = Guid.NewGuid(), RunId = Guid.NewGuid(), Stage = SdlcStage.Design };
        await _service.SendApprovalRequestAsync(gate);
    }

    [Test]
    public async Task SendApprovalRequestAsync_LogsWarning()
    {
        var gate = new StageGate { GateId = Guid.NewGuid(), RunId = Guid.NewGuid(), Stage = SdlcStage.Design };
        await _service.SendApprovalRequestAsync(gate);

        var warnCalls = _logger.ReceivedCalls().Where(c => c.GetArguments().ElementAtOrDefault(0) is LogLevel l && l == LogLevel.Warning).ToList();
        warnCalls.Should().ContainSingle();
    }
}
