using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using SDLC.Agents;
using SDLC.Contracts;
using SDLC.Infrastructure;
using SDLC.Telemetry;

namespace SDLC.Agents.Tests;

/// <summary>
/// ResearchStep fallback when all 3 attempts are unsatisfactory.
/// Verifies saved content equals last AI response, not critique text.
/// </summary>
[TestFixture, SingleThreaded]
public class ResearchStepFallbackTests
{
    private IArtifactStore _artifacts = null!;
    private IKernelFactory _kernelFactory = null!;
    private IKernel _fakeKernel = null!;
    private IPipelineTelemetry telemetry = null!;
    private IRunBudgetTracker _budgetTracker = null!;
    private ResearchBrief? _saved = null!;

    [SetUp]
    public void SetUp()
    {
        _artifacts = Substitute.For<IArtifactStore>();
        _fakeKernel = Substitute.For<IKernel>();
        _kernelFactory = Substitute.For<IKernelFactory>();
        telemetry = Substitute.For<IPipelineTelemetry>();
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

        _artifacts.SaveAsync(Arg.Do<ResearchBrief>(b => _saved = b));
    }

    [Test]
    public async Task RunAsync_AllAttemptsUnsatisfactory_SavesLastAiResponseNotCritique()
    {
        const string lastAi = "Last AI attempt content";
        const string critique = "Not satisfactory at all. [UNSATISFACTORY]";

        _fakeKernel.CompleteAsyncWithUsage(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                   .Returns(
                       ("First attempt. [UNSATISFACTORY]", TokenUsage.Zero),
                       (critique, TokenUsage.Zero),
                       ("Second attempt. [UNSATISFACTORY]", TokenUsage.Zero),
                       (critique, TokenUsage.Zero),
                       (lastAi, TokenUsage.Zero),
                       (critique, TokenUsage.Zero));

        var config = new SdlcRunConfig { ProjectBrief = "Test" };

        var step = new ResearchStep();
        await step.RunAsync(new CapturingContext(), config, _kernelFactory, _artifacts, telemetry, _budgetTracker);

        _saved!.Content.Should().Be(lastAi);
        _saved.Content.Should().NotContain("Not satisfactory");
    }

    private class CapturingContext : IKernelProcessStepContext
    {
        public Task EmitEventAsync(KernelProcessEvent @event, CancellationToken ct = default)
            => Task.CompletedTask;
    }
}
