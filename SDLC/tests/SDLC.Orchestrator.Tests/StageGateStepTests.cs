using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NUnit.Framework;
using SDLC.Contracts;
using SDLC.Infrastructure;
using SDLC.Notifications;

namespace SDLC.Orchestrator.Tests;

[TestFixture, SingleThreaded]
public class StageGateStepTests
{
    private INotificationService _notifications = null!;
    private IStageGateStore _gateStore = null!;
    private IKernelProcessStepContext _context = null!;
    private StageGateStep _step = null!;

    [SetUp]
    public void SetUp()
    {
        _notifications = Substitute.For<INotificationService>();
        _gateStore = Substitute.For<IStageGateStore>();
        _context = Substitute.For<IKernelProcessStepContext>();
        _step = new StageGateStep();
    }

    [Test]
    public async Task RequestApprovalAsync_CreatesGateInStore()
    {
        var artifact = new ResearchBrief { RunId = Guid.NewGuid(), Stage = SdlcStage.Research };
        var gate = new StageGate { GateId = Guid.NewGuid(), Status = GateStatus.Pending, Artifact = artifact };
        _gateStore.CreateGateAsync(artifact).Returns(gate);

        await _step.RequestApprovalAsync(_context, artifact, _notifications, _gateStore);

        await _gateStore.Received(1).CreateGateAsync(artifact);
    }

    [Test]
    public async Task RequestApprovalAsync_SendsNotification()
    {
        var artifact = new ResearchBrief { RunId = Guid.NewGuid(), Stage = SdlcStage.Research };
        var gate = new StageGate { GateId = Guid.NewGuid(), Status = GateStatus.Pending, Artifact = artifact };
        _gateStore.CreateGateAsync(artifact).Returns(gate);

        await _step.RequestApprovalAsync(_context, artifact, _notifications, _gateStore);

        await _notifications.Received(1).SendApprovalRequestAsync(gate);
    }

    [Test]
    public async Task RequestApprovalAsync_EmitsGatePendingEvent()
    {
        var artifact = new ResearchBrief { RunId = Guid.NewGuid(), Stage = SdlcStage.Research };
        var gate = new StageGate { GateId = Guid.NewGuid(), Status = GateStatus.Pending, Artifact = artifact };
        _gateStore.CreateGateAsync(artifact).Returns(gate);

        KernelProcessEvent? captured = null;
        _context.EmitEventAsync(Arg.Do<KernelProcessEvent>(e => captured = e));

        await _step.RequestApprovalAsync(_context, artifact, _notifications, _gateStore);

        captured!.Id.Should().Be(SdlcEvents.GatePending);
        captured.Data.Should().Be(gate.GateId);
    }

    [Test]
    public async Task RequestApprovalAsync_WhenNotificationFails_ThrowsAndGateStillCreated()
    {
        var artifact = new ResearchBrief { RunId = Guid.NewGuid(), Stage = SdlcStage.Research };
        var gate = new StageGate { GateId = Guid.NewGuid(), Status = GateStatus.Pending, Artifact = artifact };
        _gateStore.CreateGateAsync(artifact).Returns(gate);
        _notifications.SendApprovalRequestAsync(Arg.Any<StageGate>())
                      .Returns(Task.FromException<HttpRequestException>(new HttpRequestException("Slack unavailable")));

        var act = () => _step.RequestApprovalAsync(_context, artifact, _notifications, _gateStore);

        await act.Should().ThrowAsync<HttpRequestException>();
        await _gateStore.Received(1).CreateGateAsync(artifact);
    }
}
