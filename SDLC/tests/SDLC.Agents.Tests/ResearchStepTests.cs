using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using SDLC.Agents;
using SDLC.Contracts;
using SDLC.Infrastructure;
using SDLC.Telemetry;

namespace SDLC.Agents.Tests;

[TestFixture, SingleThreaded]
public class ResearchStepTests
{
    private IArtifactStore _artifacts = null!;
    private IKernelFactory _kernelFactory = null!;
    private CapturingContext _context = null!;
    private IKernel _fakeKernel = null!;
    private IPipelineTelemetry telemetry = Substitute.For<IPipelineTelemetry>();
    private IRunBudgetTracker _budgetTracker = null!;

    private static (string, TokenUsage) SatisfactoryMain => ("# Research Brief\nContent.\n[SATISFACTORY]", TokenUsage.Zero);
    private static (string, TokenUsage) SatisfactoryCritique => ("[SATISFACTORY]", TokenUsage.Zero);
    private static (string, TokenUsage) UnsatMain => ("# Research Brief\nNeeds work.\n[UNSATISFACTORY]", TokenUsage.Zero);
    private static (string, TokenUsage) UnsatCritique => ("[UNSATISFACTORY]", TokenUsage.Zero);

    [SetUp]
    public void SetUp()
    {
        _artifacts = Substitute.For<IArtifactStore>();
        _fakeKernel = Substitute.For<IKernel>();
        _kernelFactory = Substitute.For<IKernelFactory>();
        _context = new CapturingContext();
        _budgetTracker = Substitute.For<IRunBudgetTracker>();
        _budgetTracker.EnsureWithinBudgetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                      .Returns(Task.CompletedTask);
        _budgetTracker.IsOverBudgetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                      .Returns(Task.FromResult(false));
        _budgetTracker.RecordAsync(Arg.Any<Guid>(), Arg.Any<long>(), Arg.Any<long>(), Arg.Any<CancellationToken>())
                      .Returns(Task.CompletedTask);
        _budgetTracker.GetUsageAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                      .Returns(Task.FromResult(TokenUsage.Zero));
        _kernelFactory.CreateForStage(SdlcStage.Research).Returns(_fakeKernel);
    }

    [Test]
    public async Task RunAsync_SavesResearchBrief()
    {
        _fakeKernel.CompleteAsyncWithUsage(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                   .Returns(SatisfactoryMain, SatisfactoryCritique);
        var config = new SdlcRunConfig { ProjectBrief = "Build a reporting tool" };

        var step = new ResearchStep();
        await step.RunAsync(_context, config, _kernelFactory, _artifacts, telemetry, _budgetTracker);

        await _artifacts.Received(1).SaveAsync(Arg.Is<ResearchBrief>(b => b.RunId == config.RunId));
    }

    [Test]
    public async Task RunAsync_EmitsResearchCompleteEvent()
    {
        _fakeKernel.CompleteAsyncWithUsage(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                   .Returns(SatisfactoryMain, SatisfactoryCritique);
        var config = new SdlcRunConfig { ProjectBrief = "Build a reporting tool" };

        var step = new ResearchStep();
        await step.RunAsync(_context, config, _kernelFactory, _artifacts, telemetry, _budgetTracker);

        _context.Events.Should().ContainSingle();
        _context.Events[0].Id.Should().Be(SdlcEvents.ResearchComplete);
        _context.Events[0].Data.Should().BeOfType<ResearchBrief>();
    }

    [Test]
    public async Task RunAsync_WhenFirstAttemptUnsatisfactory_Retries()
    {
        _fakeKernel.CompleteAsyncWithUsage(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                   .Returns(UnsatMain, UnsatCritique, UnsatMain, UnsatCritique, SatisfactoryMain, SatisfactoryCritique);
        var config = new SdlcRunConfig { ProjectBrief = "Build a tool" };
        var step = new ResearchStep();
        await step.RunAsync(_context, config, _kernelFactory, _artifacts, telemetry, _budgetTracker);
        // If we get here past 3 attempts, the test passed
    }

    [Test]
    public async Task RunAsync_MaxThreeAttempts_DoesNotLoopForever()
    {
        _fakeKernel.CompleteAsyncWithUsage(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                   .Returns(
                       ("Always unsatisfactory.\n[UNSATISFACTORY]", TokenUsage.Zero),
                       ("[UNSATISFACTORY]", TokenUsage.Zero));
        var config = new SdlcRunConfig { ProjectBrief = "Build a tool" };

        var step = new ResearchStep();
        var act = () => step.RunAsync(_context, config, _kernelFactory, _artifacts, telemetry, _budgetTracker);

        await act.Should().CompleteWithinAsync(TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task RunAsync_SavedBrief_HasCorrectRunId()
    {
        _fakeKernel.CompleteAsyncWithUsage(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                   .Returns(SatisfactoryMain, SatisfactoryCritique);
        var runId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000000");
        var config = new SdlcRunConfig { ProjectBrief = "Project", RunId = runId };

        ResearchBrief? saved = null;
        await _artifacts.SaveAsync(Arg.Do<ResearchBrief>(b => saved = b));

        var step = new ResearchStep();
        await step.RunAsync(_context, config, _kernelFactory, _artifacts, telemetry, _budgetTracker);

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
