using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NUnit.Framework;
using SDLC.Agents;
using SDLC.Contracts;
using SDLC.Infrastructure;

namespace SDLC.Agents.Tests;

[TestFixture, SingleThreaded]
public class BuildStepTests
{
    private TestSweAfClient _sweAf = null!;
    private CapturingArtifactStore _artifacts = null!;
    private ILogger<BuildStep> _logger = null!;

    private RequirementsSpec _spec = null!;
    private ArchitectureRecord _arch = null!;

    public BuildStepTests()
    {
        var runId = Guid.NewGuid();
        _spec = new RequirementsSpec { RunId = runId, Content = "spec", Stage = SdlcStage.Requirements };
        _arch = new ArchitectureRecord { RunId = runId, Content = "arch", Stage = SdlcStage.Design };
    }

    [SetUp]
    public void SetUp()
    {
        _sweAf = new TestSweAfClient();
        _artifacts = new CapturingArtifactStore();
        _logger = NSubstitute.Substitute.For<ILogger<BuildStep>>();
    }

    [Test]
    public async Task RunAsync_SubmitsToSweAf()
    {
        _sweAf.AddStatus(new SweAfStatus { State = SweAfState.Succeeded, IsTerminal = true, Logs = "OK" });

        await new BuildStep().RunAsync(_capturingContext(), _arch, _spec, _sweAf, _artifacts, _logger);

        _sweAf.SubmitCalled.Should().BeTrue();
    }

    [Test]
    public async Task RunAsync_WhenSweAfSucceeds_SavesBuildResultWithSuccessTrue()
    {
        _sweAf.AddStatus(new SweAfStatus { State = SweAfState.Succeeded, IsTerminal = true, Logs = "OK" });

        await new BuildStep().RunAsync(_capturingContext(), _arch, _spec, _sweAf, _artifacts, _logger);

        _artifacts.Saved.Should().ContainSingle();
        _artifacts.Saved[0].Should().BeAssignableTo<BuildResult>();
        ((BuildResult)_artifacts.Saved[0]).Success.Should().BeTrue();
    }

    [Test]
    public async Task RunAsync_WhenSweAfFails_SavesBuildResultWithSuccessFalse()
    {
        _sweAf.AddStatus(new SweAfStatus { State = SweAfState.Failed, IsTerminal = true, Logs = "ERR" });

        await new BuildStep().RunAsync(_capturingContext(), _arch, _spec, _sweAf, _artifacts, _logger);

        _artifacts.Saved.Should().ContainSingle();
        ((BuildResult)_artifacts.Saved[0]).Success.Should().BeFalse();
    }

    [Test]
    public async Task RunAsync_EmitsBuildCompleteEvent()
    {
        _sweAf.AddStatus(new SweAfStatus { State = SweAfState.Succeeded, IsTerminal = true, Logs = "OK" });

        var cap = new CapturingContext();
        await new BuildStep().RunAsync(cap, _arch, _spec, _sweAf, _artifacts, _logger);

        cap.Events.Should().ContainSingle();
        cap.Events[0].Id.Should().Be(SdlcEvents.BuildComplete);
        cap.Events[0].Data.Should().BeOfType<BuildResult>();
    }

    [Test]
    public async Task RunAsync_StoresSweAfRunId()
    {
        _sweAf.AddStatus(new SweAfStatus { State = SweAfState.Succeeded, IsTerminal = true, Logs = "OK" });

        await new BuildStep().RunAsync(_capturingContext(), _arch, _spec, _sweAf, _artifacts, _logger);

        _artifacts.Saved.Should().ContainSingle();
        ((BuildResult)_artifacts.Saved[0]).SweAfRunId.Should().Be("run-001");
    }

    [Test]
    public async Task RunAsync_BuildResultRunId_MatchesSpecRunId()
    {
        _sweAf.AddStatus(new SweAfStatus { State = SweAfState.Succeeded, IsTerminal = true, Logs = "OK" });

        await new BuildStep().RunAsync(_capturingContext(), _arch, _spec, _sweAf, _artifacts, _logger);

        _artifacts.Saved.Should().ContainSingle();
        ((BuildResult)_artifacts.Saved[0]).RunId.Should().Be(_spec.RunId);
    }

    private IKernelProcessStepContext _capturingContext() => new CapturingContext();

    private class CapturingContext : IKernelProcessStepContext
    {
        public List<KernelProcessEvent> Events { get; } = new();

        public Task EmitEventAsync(KernelProcessEvent @event, CancellationToken ct = default)
        {
            Events.Add(@event);
            return Task.CompletedTask;
        }
    }

    private class CapturingArtifactStore : IArtifactStore
    {
        public List<SdlcArtifact> Saved { get; } = new();

        public Task SaveAsync(SdlcArtifact artifact)
        {
            Saved.Add(artifact);
            return Task.CompletedTask;
        }

        public Task<T?> GetAsync<T>(Guid artifactId) where T : SdlcArtifact => throw new NotImplementedException();
        public Task<T?> GetLatestForRunAsync<T>(Guid runId) where T : SdlcArtifact => throw new NotImplementedException();
        public Task UpdateStatusAsync(Guid artifactId, ArtifactStatus status) => throw new NotImplementedException();
        public Task UpdateContentAsync(Guid artifactId, string content) => throw new NotImplementedException();
        public Task<List<SdlcArtifact>> GetAllForRunAsync(Guid runId) => throw new NotImplementedException();
    }

    private class TestSweAfClient : ISweAfClient
    {
        private readonly List<SweAfStatus> _statuses = new();
        private readonly TaskCompletionSource _signal = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool SubmitCalled { get; private set; }
        public bool PollCalled { get; private set; }

        public void AddStatus(SweAfStatus status) => _statuses.Add(status);

        public Task<string> SubmitAsync(SweAfTask task, CancellationToken ct = default)
        {
            SubmitCalled = true;
            return Task.FromResult("run-001");
        }

        public async IAsyncEnumerable<SweAfStatus> PollAsync(string runId, CancellationToken ct = default)
        {
            PollCalled = true;
            // Brief delay to let test framework settle
            await Task.Delay(1);
            foreach (var s in _statuses)
                yield return s;
        }
    }
}
