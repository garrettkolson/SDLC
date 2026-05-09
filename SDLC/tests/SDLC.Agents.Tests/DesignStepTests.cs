using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using SDLC.Agents;
using SDLC.Contracts;
using SDLC.Infrastructure;
using SDLC.Telemetry;

namespace SDLC.Agents.Tests;

/// <summary>
/// DesignStep full cycle.
/// </summary>
[TestFixture, SingleThreaded]
public class DesignStepTests
{
    private IArtifactStore _artifacts = null!;
    private IKernelFactory _kernelFactory = null!;
    private IKernel _fakeKernel = null!;
    private IPipelineTelemetry telemetry = null!;
    private IRunBudgetTracker _budgetTracker = null!;
    private ArchitectureRecord? _saved = null!;
    private KernelProcessEvent? _eventCaptured = null!;
    private CapturingContext _ctx = null!;
    private SdlcRunConfig _config = null!;
    private ResearchBrief _research = null!;
    private RequirementsSpec _spec = null!;

    private static (string, TokenUsage) Sat => ("System uses JWT auth. [SATISFACTORY]", TokenUsage.Zero);
    private static (string, TokenUsage) SatCritique => ("[SATISFACTORY]", TokenUsage.Zero);

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
        _kernelFactory.CreateForStage(SdlcStage.Design).Returns(_fakeKernel);

        _artifacts.SaveAsync(Arg.Do<ArchitectureRecord>(r => _saved = r));

        _ctx = new CapturingContext();
        _ctx.OnEvent = (e) => _eventCaptured = e;

        _config = new SdlcRunConfig { ProjectBrief = "Test" };
        _research = new ResearchBrief { Content = "research" };
        _spec = new RequirementsSpec { Content = "spec" };
    }

    [Test]
    public async Task RunAsync_SavesArchitectureRecord()
    {
        _fakeKernel.CompleteAsyncWithUsage(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                   .Returns(Sat, SatCritique);

        await new DesignStep().RunAsync(_ctx, _config, _research, _spec, _kernelFactory, _artifacts, telemetry, _budgetTracker);

        _saved.Should().NotBeNull();
        _saved!.Content.Should().Be("System uses JWT auth. [SATISFACTORY]");
    }

    [Test]
    public async Task RunAsync_EmitsDesignCompleteEvent()
    {
        _fakeKernel.CompleteAsyncWithUsage(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                   .Returns(Sat, SatCritique);

        await new DesignStep().RunAsync(_ctx, _config, _research, _spec, _kernelFactory, _artifacts, telemetry, _budgetTracker);

        _eventCaptured!.Id.Should().Be(SdlcEvents.DesignComplete);
        _eventCaptured.Data.Should().BeAssignableTo<ArchitectureRecord>();
    }

    [Test]
    public async Task RunAsync_Fallback_UsesLastAiResponse()
    {
        const string lastResponse = "last design attempt";
        _fakeKernel.CompleteAsyncWithUsage(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                   .Returns(
                       (lastResponse + " [UNSATISFACTORY]", TokenUsage.Zero),
                       ("[UNSATISFACTORY]", TokenUsage.Zero),
                       (lastResponse + " [UNSATISFACTORY]", TokenUsage.Zero),
                       ("[UNSATISFACTORY]", TokenUsage.Zero),
                       (lastResponse + " [UNSATISFACTORY]", TokenUsage.Zero),
                       ("[UNSATISFACTORY]", TokenUsage.Zero));

        await new DesignStep().RunAsync(_ctx, _config, _research, _spec, _kernelFactory, _artifacts, telemetry, _budgetTracker);

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
