using Microsoft.Extensions.Logging;
using SDLC.Agents;
using SDLC.Contracts;
using SDLC.Infrastructure;
using SDLC.Notifications;
using SDLC.Telemetry;

namespace SDLC.Orchestrator;

public class GateRejectedException(Guid gateId, string? notes)
    : Exception($"Gate {gateId} was rejected: {notes}")
{
    public Guid GateId { get; } = gateId;
}

public class SdlcProcessFactory(
    IKernelFactory kernelFactory,
    IArtifactStore artifactStore,
    IStageGateStore gateStore,
    INotificationService notifications,
    ISweAfClient sweAfClient,
    IRunStore runStore,
    IPipelineTelemetry telemetry,
    PipelineRunnerService runner,
    ILoggerFactory loggerFactory,
    ILogger<SdlcProcessFactory> logger) : ISdlcProcessFactory
{
    public ProcessHandle StartAsync(SdlcRunConfig config, CancellationToken ct = default)
    {
        var task = RunPipelineAsync(config, ct);
        return new ProcessHandle(task);
    }

    public ProcessHandle ResumeAsync(SdlcRunConfig config, string stage, CancellationToken ct = default)
    {
        var task = ResumePipelineAsync(config, stage, ct);
        return new ProcessHandle(task);
    }

    private async Task RunPipelineAsync(SdlcRunConfig config, CancellationToken ct)
    {
        await runStore.CreateRunAsync(config.RunId, config.ProjectBrief, DateTimeOffset.UtcNow.ToString("o"));

        logger.LogInformation("Pipeline started for run {RunId}", config.RunId);

        try
        {
            // Stage 1: Research
            var researchContext = new CapturingContext();
            await new ResearchStep().RunAsync(researchContext, config, kernelFactory, artifactStore, telemetry, ct);
            var research = (ResearchBrief)researchContext.LastEvent!.Data!;
            await runStore.UpdateStageAsync(config.RunId, "Research", "Running");

            // Stage 2: Requirements
            var reqContext = new CapturingContext();
            await new RequirementsStep().RunAsync(reqContext, config, research, kernelFactory, artifactStore, telemetry, ct);
            var spec = (RequirementsSpec)reqContext.LastEvent!.Data!;
            await runStore.UpdateStageAsync(config.RunId, "Requirements", "Running");

            // Gate: Requirements -> Design
            await WaitForGateWithApprovalAsync(spec, ct);

            // Stage 3: Design
            var latestSpec = await artifactStore.GetLatestForRunAsync<RequirementsSpec>(config.RunId) ?? spec;
            var designContext = new CapturingContext();
            await new DesignStep().RunAsync(designContext, config, research, latestSpec, kernelFactory, artifactStore, telemetry, ct);
            var architecture = (ArchitectureRecord)designContext.LastEvent!.Data!;
            await runStore.UpdateStageAsync(config.RunId, "Design", "Running");

            // Gate: Design -> Build
            await WaitForGateWithApprovalAsync(architecture, ct);

            // Stage 4: Build
            var buildContext = new CapturingContext();
            await new BuildStep().RunAsync(
                buildContext, architecture, latestSpec, sweAfClient, artifactStore, telemetry, loggerFactory.CreateLogger<BuildStep>(), ct);
            var buildResult = (BuildResult)buildContext.LastEvent!.Data!;
            await runStore.UpdateStageAsync(config.RunId, "Build", "Running");

            // Stage 5: Learn
            var learnContext = new CapturingContext();
            await new LearnStep().RunAsync(
                learnContext, config, latestSpec, buildResult, kernelFactory, artifactStore, telemetry, ct);
            await runStore.UpdateStageAsync(config.RunId, "Learn", "Completed");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Pipeline failed for run {RunId}", config.RunId);
            await runStore.UpdateStageAsync(config.RunId, "Failed", "Failed");
            throw;
        }

        logger.LogInformation("Pipeline complete for run {RunId}", config.RunId);
    }

    private async Task ResumePipelineAsync(SdlcRunConfig config, string stage, CancellationToken ct)
    {
        logger.LogInformation("Resuming pipeline for run {RunId} at stage {Stage}", config.RunId, stage);

        ResearchBrief? research = null;
        RequirementsSpec? spec = null;
        ArchitectureRecord? architecture = null;

        try
        {
            // Research always complete before any resume point
            var existingResearch = await artifactStore.GetLatestForRunAsync<ResearchBrief>(config.RunId);
            if (existingResearch != null)
                research = (ResearchBrief)existingResearch;
            else
            {
                var researchContext = new CapturingContext();
                await new ResearchStep().RunAsync(researchContext, config, kernelFactory, artifactStore, telemetry, ct);
                research = (ResearchBrief)researchContext.LastEvent!.Data!;
                await runStore.UpdateStageAsync(config.RunId, "Research", "Running");
            }

            // Requirements always complete before any resume point
            var existingSpec = await artifactStore.GetLatestForRunAsync<RequirementsSpec>(config.RunId);
            if (existingSpec != null)
                spec = (RequirementsSpec)existingSpec;
            else
            {
                var reqContext = new CapturingContext();
                await new RequirementsStep().RunAsync(reqContext, config, research!, kernelFactory, artifactStore, telemetry, ct);
                spec = (RequirementsSpec)reqContext.LastEvent!.Data!;
                await runStore.UpdateStageAsync(config.RunId, "Requirements", "Running");
            }

            switch (stage)
            {
                case "Research":
                    await runStore.UpdateStageAsync(config.RunId, "Research", "Completed");
                    break;

                case "Requirements":
                    await runStore.UpdateStageAsync(config.RunId, "Requirements", "Completed");
                    break;

                case "Design":
                {
                    var latestSpec = await artifactStore.GetLatestForRunAsync<RequirementsSpec>(config.RunId) ?? spec!;
                    var designContext = new CapturingContext();
                    await new DesignStep().RunAsync(designContext, config, research!, latestSpec, kernelFactory, artifactStore, telemetry, ct);
                    architecture = (ArchitectureRecord)designContext.LastEvent!.Data!;
                    await runStore.UpdateStageAsync(config.RunId, "Design", "Completed");
                    break;
                }

                case "Build":
                {
                    var latestSpec2 = await artifactStore.GetLatestForRunAsync<RequirementsSpec>(config.RunId) ?? spec!;
                    var existingArch = await artifactStore.GetLatestForRunAsync<ArchitectureRecord>(config.RunId);
                    if (existingArch != null)
                        architecture = (ArchitectureRecord)existingArch;
                    var buildContext = new CapturingContext();
                    await new BuildStep().RunAsync(
                        buildContext, architecture!, latestSpec2, sweAfClient, artifactStore, telemetry, loggerFactory.CreateLogger<BuildStep>(), ct);
                    var buildResult = (BuildResult)buildContext.LastEvent!.Data!;
                    await runStore.UpdateStageAsync(config.RunId, "Build", "Completed");
                    break;
                }

                case "Learn":
                {
                    var latestSpec3 = await artifactStore.GetLatestForRunAsync<RequirementsSpec>(config.RunId) ?? spec!;
                    var existingArch2 = await artifactStore.GetLatestForRunAsync<ArchitectureRecord>(config.RunId);
                    ArchitectureRecord arch = existingArch2 ?? throw new InvalidOperationException("No architecture artifact for Learn stage resume");
                    var buildContext2 = new CapturingContext();
                    await new BuildStep().RunAsync(
                        buildContext2, arch, latestSpec3, sweAfClient, artifactStore, telemetry, loggerFactory.CreateLogger<BuildStep>(), ct);
                    var buildResult = (BuildResult)buildContext2.LastEvent!.Data!;
                    var learnContext = new CapturingContext();
                    await new LearnStep().RunAsync(
                        learnContext, config, latestSpec3, buildResult, kernelFactory, artifactStore, telemetry, ct);
                    await runStore.UpdateStageAsync(config.RunId, "Learn", "Completed");
                    break;
                }

                default:
                    throw new InvalidOperationException($"Unknown resume stage: {stage}");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Resume pipeline failed for run {RunId}", config.RunId);
            await runStore.UpdateStageAsync(config.RunId, stage, "Failed");
            throw;
        }

        logger.LogInformation("Resume pipeline complete for run {RunId}", config.RunId);
    }

    private async Task WaitForGateWithApprovalAsync(SdlcArtifact artifact, CancellationToken ct)
    {
        var gate = await gateStore.CreateGateAsync(artifact);

        try
        {
            await notifications.SendApprovalRequestAsync(gate);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Notification failed for gate {GateId}. Gate remains pending.", gate.GateId);
        }

        var resolution = await runner.WaitForGateAsync(gate.GateId, ct);
        await gateStore.ResolveAsync(gate.GateId, resolution.Decision, resolution.Notes, "system", "system");

        if (resolution.Decision == GateDecision.Rejected)
            throw new GateRejectedException(gate.GateId, resolution.Notes);
    }

    private sealed class CapturingContext : IKernelProcessStepContext
    {
        public KernelProcessEvent? LastEvent { get; private set; }

        public Task EmitEventAsync(KernelProcessEvent @event, CancellationToken ct = default)
        {
            LastEvent = @event;
            return Task.CompletedTask;
        }
    }
}
