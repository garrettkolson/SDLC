using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NUnit.Framework;
using SDLC.Agents;
using SDLC.Contracts;
using SDLC.Infrastructure;

namespace SDLC.Agents.Tests;

[TestFixture, SingleThreaded]
public class ResearchStepTests
{
    private IArtifactStore _artifacts = null!;
    private IKernelFactory _kernelFactory = null!;
    private CapturingContext _context = null!;
    private IKernel _fakeKernel = null!;

    [SetUp]
    public void SetUp()
    {
        _artifacts = Substitute.For<IArtifactStore>();
        _fakeKernel = Substitute.For<IKernel>();
        _kernelFactory = Substitute.For<IKernelFactory>();
        _context = new CapturingContext();
        _kernelFactory.CreateForStage(SdlcStage.Research).Returns(_fakeKernel);
    }

    [Test]
    public async Task RunAsync_SavesResearchBrief()
    {
        _fakeKernel.CompleteAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                   .Returns("# Research Brief\nContent here.\n[SATISFACTORY]");
        var config = new SdlcRunConfig { ProjectBrief = "Build a reporting tool" };

        var step = new ResearchStep();
        await step.RunAsync(_context, config, _kernelFactory, _artifacts);

        await _artifacts.Received(1).SaveAsync(Arg.Is<ResearchBrief>(b => b.RunId == config.RunId));
    }

    [Test]
    public async Task RunAsync_EmitsResearchCompleteEvent()
    {
        _fakeKernel.CompleteAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                   .Returns("# Research Brief\nContent.\n[SATISFACTORY]");
        var config = new SdlcRunConfig { ProjectBrief = "Build a reporting tool" };

        var step = new ResearchStep();
        await step.RunAsync(_context, config, _kernelFactory, _artifacts);

        _context.Events.Should().ContainSingle();
        _context.Events[0].Id.Should().Be(SdlcEvents.ResearchComplete);
        _context.Events[0].Data.Should().BeOfType<ResearchBrief>();
    }

    [Test]
    public async Task RunAsync_WhenFirstAttemptUnsatisfactory_Retries()
    {
        var callCount = 0;
        _fakeKernel.CompleteAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                   .Returns(inv =>
                   {
                       callCount++;
                       return callCount < 3
                           ? "Needs improvement. [UNSATISFACTORY]"
                           : "Good. [SATISFACTORY]";
                   });

        var config = new SdlcRunConfig { ProjectBrief = "Build a tool" };
        var step = new ResearchStep();
        await step.RunAsync(_context, config, _kernelFactory, _artifacts);

        callCount.Should().BeGreaterThanOrEqualTo(3);
    }

    [Test]
    public async Task RunAsync_MaxThreeAttempts_DoesNotLoopForever()
    {
        _fakeKernel.CompleteAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                   .Returns("Always unsatisfactory. [UNSATISFACTORY]");
        var config = new SdlcRunConfig { ProjectBrief = "Build a tool" };

        var step = new ResearchStep();
        var act = () => step.RunAsync(_context, config, _kernelFactory, _artifacts);

        await act.Should().CompleteWithinAsync(TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task RunAsync_SavedBrief_HasCorrectRunId()
    {
        _fakeKernel.CompleteAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                   .Returns("Good research. [SATISFACTORY]");
        var runId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000000");
        var config = new SdlcRunConfig { ProjectBrief = "Project", RunId = runId };

        ResearchBrief? saved = null;
        await _artifacts.SaveAsync(Arg.Do<ResearchBrief>(b => saved = b));

        var step = new ResearchStep();
        await step.RunAsync(_context, config, _kernelFactory, _artifacts);

        saved!.RunId.Should().Be(runId);
    }

    private class CapturingContext : IKernelProcessStepContext
    {
        public List<KernelProcessEvent> Events { get; } = new();

        public Task EmitEventAsync(KernelProcessEvent @event, CancellationToken ct = default)
        {
            Events.Add(@event);
            return Task.CompletedTask;
        }
    }
}
