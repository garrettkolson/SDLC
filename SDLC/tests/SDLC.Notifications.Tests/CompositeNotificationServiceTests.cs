using System.Net;
using FluentAssertions;
using NUnit.Framework;
using SDLC.Contracts;
using SDLC.Infrastructure;
using SDLC.Notifications;

namespace SDLC.Notifications.Tests;

[TestFixture]
public class CompositeNotificationServiceTests
{
    [Test]
    public async Task SendApprovalRequestAsync_FallsThroughToEmail_WhenSlackFails()
    {
        var testEmail = new TestEmailNotificationService();
        var slack = new SlackNotificationService(
            new HttpClient(new FailingHandler()),
            new DashboardUrlBuilder("http://localhost:1234"));
        var service = new CompositeNotificationService(slack, testEmail, null);

        var gate = new StageGate
        {
            RunId = Guid.NewGuid(),
            GateId = Guid.NewGuid(),
            Stage = SdlcStage.Research,
            Status = GateStatus.Pending
        };

        await service.SendApprovalRequestAsync(gate);

        testEmail.Sent.Should().Be(true);
    }

    [Test]
    public async Task SendApprovalRequestAsync_ThrowsComposite_WhenBothFail()
    {
        var testEmail = new TestEmailNotificationService { ShouldFail = true };
        var slack = new SlackNotificationService(
            new HttpClient(new FailingHandler()),
            new DashboardUrlBuilder("http://localhost:1234"));
        var service = new CompositeNotificationService(slack, testEmail, null);

        var gate = new StageGate
        {
            RunId = Guid.NewGuid(),
            GateId = Guid.NewGuid(),
            Stage = SdlcStage.Research,
            Status = GateStatus.Pending
        };

        var act = () => service.SendApprovalRequestAsync(gate);

        await act.Should().ThrowAsync<CompositeNotificationException>();
    }

    private class FailingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("slack down");
        }
    }

    private class TestEmailNotificationService : IEmailNotificationService
    {
        private bool _sent = false;

        public bool Sent => _sent;
        public bool ShouldFail { get; set; }

        public Task SendApprovalRequestAsync(StageGate gate, CancellationToken _ = default)
        {
            if (ShouldFail)
                throw new InvalidOperationException("email fail");
            _sent = true;
            return Task.CompletedTask;
        }
    }
}
