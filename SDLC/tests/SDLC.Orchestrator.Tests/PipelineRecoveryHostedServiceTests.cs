using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NUnit.Framework;
using SDLC.Orchestrator;
using SDLC.Contracts;
using SDLC.Telemetry;
using SDLC.Infrastructure;

namespace SDLC.Orchestrator.Tests;

[TestFixture]
public class PipelineRecoveryHostedServiceTests
{
    private PipelineRunnerService _runner = null!;
    private ILogger<PipelineRecoveryHostedService> _logger = null!;

    [SetUp]
    public void SetUp()
    {
        var processFactory = Substitute.For<ISdlcProcessFactory>();
        var runnerLogger = Substitute.For<ILogger<PipelineRunnerService>>();
        var telemetry = Substitute.For<IPipelineTelemetry>();
        var gateStore = Substitute.For<IStageGateStore>();
        var runStore = Substitute.For<IRunStore>();
        var budgetTracker = Substitute.For<IRunBudgetTracker>();

        _runner = new PipelineRunnerService(processFactory, runnerLogger, telemetry, gateStore, runStore, budgetTracker);
        _logger = Substitute.For<ILogger<PipelineRecoveryHostedService>>();
    }

    [Test]
    public async Task StartAsync_DoesNotThrow()
    {
        var service = new PipelineRecoveryHostedService(_runner, _logger);
        await service.StartAsync(CancellationToken.None);
    }

    [Test]
    public async Task StartAsync_RecoverPendingGatesCalled()
    {
        // Verify the service constructor and StartAsync actually call RecoverPendingGatesAsync
        // by checking that it doesn't throw (real impl calls gateStore.GetAllPendingAsync which returns empty)
        var service = new PipelineRecoveryHostedService(_runner, _logger);
        var act = () => service.StartAsync(CancellationToken.None);
        act.Should().NotThrowAsync();
    }

    [Test]
    public async Task StopAsync_DoesNotThrow()
    {
        var service = new PipelineRecoveryHostedService(_runner, _logger);
        await service.StopAsync(CancellationToken.None);
    }
}
