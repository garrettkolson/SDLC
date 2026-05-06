using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NUnit.Framework;
using SDLC.Agents;
using SDLC.Contracts;
using SDLC.Infrastructure;
using SDLC.Telemetry;

namespace SDLC.Agents.Tests;

/// <summary>
/// BuildStep null result guard: empty poll stream produces BuildResult with Success=false.
/// </summary>
[TestFixture, SingleThreaded]
public class BuildStepNullGuardTests
{
    private ILogger<BuildStep> _logger = null!;
    private CapturingArtifactStore _artifacts = null!;
    private IPipelineTelemetry telemetry = null!;

    [SetUp]
    public void SetUp()
    {
        _logger = Substitute.For<ILogger<BuildStep>>();
        _artifacts = new CapturingArtifactStore();
        telemetry = Substitute.For<IPipelineTelemetry>();
    }

    [Test]
    public async Task RunAsync_PollAsyncReturnsNoTerminalStatus_SavesBuildResultWithSuccessFalse()
    {
        var runId = Guid.NewGuid();
        var spec = new RequirementsSpec { RunId = runId, Content = "spec", Stage = SdlcStage.Requirements };
        var arch = new ArchitectureRecord { RunId = runId, Content = "arch", Stage = SdlcStage.Design };

        var sweAf = new EmptyPollSweAfClient();
        await new BuildStep().RunAsync(new CapturingContext(), arch, spec, sweAf, _artifacts, telemetry, _logger);

        _artifacts.Saved.Should().ContainSingle();
        var result = ((BuildResult)_artifacts.Saved[0]);
        result.Success.Should().BeFalse();
        result.Logs.Should().Contain("timed out");
    }

    private class CapturingContext : IKernelProcessStepContext
    {
        public Task EmitEventAsync(KernelProcessEvent @event, CancellationToken ct = default) => Task.CompletedTask;
    }

    private class CapturingArtifactStore : IArtifactStore
    {
        public List<SdlcArtifact> Saved { get; } = new();
        public Task SaveAsync(SdlcArtifact artifact) { Saved.Add(artifact); return Task.CompletedTask; }
        public Task<T?> GetAsync<T>(Guid artifactId) where T : SdlcArtifact => throw new NotImplementedException();
        public Task<T?> GetLatestForRunAsync<T>(Guid runId) where T : SdlcArtifact => throw new NotImplementedException();
        public Task UpdateStatusAsync(Guid artifactId, ArtifactStatus status) => throw new NotImplementedException();
        public Task UpdateContentAsync(Guid artifactId, string content) => throw new NotImplementedException();
        public Task<List<SdlcArtifact>> GetAllForRunAsync(Guid runId) => throw new NotImplementedException();
        public Task<List<Guid>> GetAllRunIdsAsync() => Task.FromResult(new List<Guid>());
    }

    private class EmptyPollSweAfClient : ISweAfClient
    {
        public Task<string> SubmitAsync(SweAfTask task, CancellationToken ct = default) => Task.FromResult("run-001");
        public async IAsyncEnumerable<SweAfStatus> PollAsync(string runId, CancellationToken ct = default)
        {
            // Never yields a terminal status
            await Task.CompletedTask;
            yield break;
        }
    }
}
