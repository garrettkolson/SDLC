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
    PipelineRunnerService runner,
    ILogger<SdlcProcessFactory> logger,
    IPipelineTelemetry? telemetry = null) : ISdlcProcessFactory
{
    public ProcessHandle StartAsync(SdlcRunConfig config)
    {
        var task = RunPipelineAsync(config, CancellationToken.None);
        return new ProcessHandle(task);
    }

    private async Task RunPipelineAsync(SdlcRunConfig config, CancellationToken ct)
    {
        if (telemetry != null)
            await telemetry.StartPipelineRunAsync(config.RunId, config.ProjectBrief, ct);
        logger.LogInformation("Pipeline started for run {RunId}", config.RunId);
        SdlcTelemetry.RunsStarted.Add(1);

        try
        {
            // Stage 1: Research
            var researchContext = new CapturingContext();
            await new ResearchStep().RunAsync(researchContext, config, kernelFactory, artifactStore, telemetry, ct);
            var research = (ResearchBrief)researchContext.LastEvent!.Data!;

            // Stage 2: Requirements
            var reqContext = new CapturingContext();
            await new RequirementsStep().RunAsync(reqContext, config, research, kernelFactory, artifactStore, telemetry, ct);
            var spec = (RequirementsSpec)reqContext.LastEvent!.Data!;

            // Gate: Requirements -> Design
            await WaitForGateWithApprovalAsync(spec, ct);

            // Stage 3: Design
            var latestSpec = await artifactStore.GetLatestForRunAsync<RequirementsSpec>(config.RunId) ?? spec;
            var designContext = new CapturingContext();
            await new DesignStep().RunAsync(designContext, config, research, latestSpec, kernelFactory, artifactStore, telemetry, ct);
            var architecture = (ArchitectureRecord)designContext.LastEvent!.Data!;

            // Gate: Design -> Build
            await WaitForGateWithApprovalAsync(architecture, ct);

            // Stage 4: Build - placeholder (needs ISweAfClient)
            throw new NotImplementedException("Wire ISweAfClient into SdlcProcessFactory");

            // Stage 5: Learn - placeholder
            // var learnContext = new CapturingContext();
            // await new LearnStep().RunAsync(learnContext, config, latestSpec, buildResult, kernelFactory, artifactStore, telemetry, ct);

            logger.LogInformation("Pipeline complete for run {RunId}", config.RunId);
            if (telemetry != null)
                await telemetry.CompletePipelineRunAsync(config.RunId, ct);
            SdlcTelemetry.RunsCompleted.Add(1);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Pipeline failed for run {RunId}", config.RunId);
            if (telemetry != null)
                await telemetry.CompletePipelineRunAsync(config.RunId, ct);
            SdlcTelemetry.RunsCompleted.Add(1);
            throw;
        }
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
        await gateStore.ResolveAsync(gate.GateId, resolution.Decision, resolution.Notes);

        if (resolution.Decision == GateDecision.Approved && telemetry != null)
            await telemetry.RecordGateApprovedAsync(gate.GateId, ct);
        else if (resolution.Decision == GateDecision.Rejected && telemetry != null)
            await telemetry.RecordGateRejectedAsync(gate.GateId, ct);

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
