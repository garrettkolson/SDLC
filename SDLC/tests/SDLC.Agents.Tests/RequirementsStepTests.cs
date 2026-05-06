using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using SDLC.Agents;
using SDLC.Contracts;
using SDLC.Infrastructure;
using SDLC.Telemetry;

namespace SDLC.Agents.Tests;

/// <summary>
/// RequirementsStep full cycle.
/// </summary>
[TestFixture, SingleThreaded]
public class RequirementsStepTests
{
    private IArtifactStore _artifacts = null!;
    private IKernelFactory _kernelFactory = null!;
    private IKernel _fakeKernel = null!;
    private IPipelineTelemetry telemetry = null!;
    private RequirementsSpec? _saved = null!;
    private KernelProcessEvent? _eventCaptured = null!;
    private CapturingContext _ctx = null!;
    private SdlcRunConfig _config = null!;
    private ResearchBrief _research = null!;

    [SetUp]
    public void SetUp()
    {
        _artifacts = Substitute.For<IArtifactStore>();
        _fakeKernel = Substitute.For<IKernel>();
        _kernelFactory = Substitute.For<IKernelFactory>();
        telemetry = Substitute.For<IPipelineTelemetry>();
        _kernelFactory.CreateForStage(SdlcStage.Requirements).Returns(_fakeKernel);

        _artifacts.SaveAsync(Arg.Do<RequirementsSpec>(s => _saved = s));

        _ctx = new CapturingContext();
        _ctx.OnEvent = (e) => _eventCaptured = e;

        _config = new SdlcRunConfig { ProjectBrief = "Test project" };
        _research = new ResearchBrief { Content = "research findings" };
    }

    [Test]
    public async Task RunAsync_SavesRequirementsSpec()
    {
        _fakeKernel.CompleteAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                   .Returns("Requirements content. [SATISFACTORY]");

        await new RequirementsStep().RunAsync(_ctx, _config, _research, _kernelFactory, _artifacts, telemetry);

        _saved.Should().NotBeNull();
        _saved!.Content.Should().Be("Requirements content. [SATISFACTORY]");
    }

    [Test]
    public async Task RunAsync_EmitsRequirementsCompleteEvent()
    {
        _fakeKernel.CompleteAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                   .Returns("Requirements. [SATISFACTORY]");

        await new RequirementsStep().RunAsync(_ctx, _config, _research, _kernelFactory, _artifacts, telemetry);

        _eventCaptured!.Id.Should().Be(SdlcEvents.RequirementsComplete);
        _eventCaptured.Data.Should().BeAssignableTo<RequirementsSpec>();
    }

    [Test]
    public async Task RunAsync_OnUnsatisfactory_RetriesUntilSatisfactory()
    {
        var callCount = 0;
        _fakeKernel.CompleteAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                   .Returns(_ =>
                   {
                       callCount++;
                       return callCount < 3 ? "bad. [UNSATISFACTORY]" : "good. [SATISFACTORY]";
                   });

        await new RequirementsStep().RunAsync(_ctx, _config, _research, _kernelFactory, _artifacts, telemetry);

        callCount.Should().BeGreaterThanOrEqualTo(3);
    }

    [Test]
    public async Task RunAsync_Fallback_UsesLastAiResponse()
    {
        const string lastResponse = "last attempt";
        _fakeKernel.CompleteAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                   .Returns(lastResponse + " [UNSATISFACTORY]");

        await new RequirementsStep().RunAsync(_ctx, _config, _research, _kernelFactory, _artifacts, telemetry);

        _saved!.Content.Should().Be(lastResponse + " [UNSATISFACTORY]");
    }

    private class CapturingContext : IKernelProcessStepContext
    {
        public Action<KernelProcessEvent>? OnEvent;
        public Task EmitEventAsync(KernelProcessEvent @event, CancellationToken ct = default)
        {
            OnEvent?.Invoke(@event);
            return Task.CompletedTask;
        }
    }
}
