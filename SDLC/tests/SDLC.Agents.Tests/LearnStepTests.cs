using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using SDLC.Agents;
using SDLC.Contracts;
using SDLC.Infrastructure;
using SDLC.Telemetry;

namespace SDLC.Agents.Tests;

/// <summary>
/// LearnStep full cycle.
/// </summary>
[TestFixture, SingleThreaded]
public class LearnStepTests
{
    private IArtifactStore _artifacts = null!;
    private IKernelFactory _kernelFactory = null!;
    private IKernel _fakeKernel = null!;
    private IPipelineTelemetry telemetry = null!;
    private LearnReport? _saved = null!;
    private KernelProcessEvent? _eventCaptured = null!;
    private CapturingContext _ctx = null!;
    private SdlcRunConfig _config = null!;
    private RequirementsSpec _spec = null!;
    private BuildResult _build = null!;

    [SetUp]
    public void SetUp()
    {
        _artifacts = Substitute.For<IArtifactStore>();
        _fakeKernel = Substitute.For<IKernel>();
        _kernelFactory = Substitute.For<IKernelFactory>();
        telemetry = Substitute.For<IPipelineTelemetry>();
        _kernelFactory.CreateForStage(SdlcStage.Learn).Returns(_fakeKernel);

        _artifacts.SaveAsync(Arg.Do<LearnReport>(r => _saved = r));

        _ctx = new CapturingContext();
        _ctx.OnEvent = (e) => _eventCaptured = e;

        _config = new SdlcRunConfig { ProjectBrief = "Test" };
        _spec = new RequirementsSpec { Content = "spec" };
        _build = new BuildResult { Logs = "logs" };
    }

    [Test]
    public async Task RunAsync_SavesLearnReport()
    {
        _fakeKernel.CompleteAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                   .Returns("Key learning: test coverage was low. [SATISFACTORY]");

        await new LearnStep().RunAsync(_ctx, _config, _spec, _build, _kernelFactory, _artifacts, telemetry);

        _saved.Should().NotBeNull();
        _saved!.Content.Should().Be("Key learning: test coverage was low. [SATISFACTORY]");
    }

    [Test]
    public async Task RunAsync_EmitsLearnCompleteEvent()
    {
        _fakeKernel.CompleteAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                   .Returns("learned. [SATISFACTORY]");

        await new LearnStep().RunAsync(_ctx, _config, _spec, _build, _kernelFactory, _artifacts, telemetry);

        _eventCaptured!.Id.Should().Be(SdlcEvents.LearnComplete);
        _eventCaptured.Data.Should().BeAssignableTo<LearnReport>();
    }

    [Test]
    public async Task RunAsync_Fallback_UsesLastAiResponse()
    {
        const string lastResponse = "last learn attempt";
        _fakeKernel.CompleteAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                   .Returns(lastResponse + " [UNSATISFACTORY]");

        await new LearnStep().RunAsync(_ctx, _config, _spec, _build, _kernelFactory, _artifacts, telemetry);

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
