using SDLC.Contracts;
using SDLC.Infrastructure;
using SDLC.Telemetry;

namespace SDLC.Agents;

public class DesignStep
{
    private const int MaxAttempts = 3;

    public async Task RunAsync(
        IKernelProcessStepContext context,
        SdlcRunConfig config,
        ResearchBrief research,
        RequirementsSpec spec,
        IKernelFactory kernelFactory,
        IArtifactStore artifacts,
        IPipelineTelemetry? telemetry = null,
        CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var kernel = kernelFactory.CreateForStage(SdlcStage.Design);
            var history = new List<string> { DesignPrompts.BuildPrompt(config.ProjectBrief, research.Content, spec.Content) };

            ArchitectureRecord? record = null;
            var lastAiResponse = "";

            for (var attempt = 0; attempt < MaxAttempts; attempt++)
            {
                var response = await kernel.CompleteAsync(DesignPrompts.SystemPrompt, string.Join("\n", history), ct);
                lastAiResponse = response;
                history.Add($"AI: {response}");

                var critique = await kernel.CompleteAsync(DesignPrompts.CritiquePrompt, response, ct);

                if (DesignPrompts.IsSatisfactory(critique))
                {
                    record = new ArchitectureRecord { Content = response, RunId = config.RunId, Stage = SdlcStage.Design };
                    break;
                }
                history.Add($"Critique: {critique}");
            }

            record ??= new ArchitectureRecord { Content = lastAiResponse, RunId = config.RunId, Stage = SdlcStage.Design };

            await artifacts.SaveAsync(record);
            await context.EmitEventAsync(new KernelProcessEvent { Id = SdlcEvents.DesignComplete, Data = record }, ct);
            if (telemetry != null)
                await telemetry.RecordStepCompletedAsync(SdlcStage.Design, nameof(DesignStep), ct);
        }
        catch (Exception ex)
        {
            if (telemetry != null)
                await telemetry.RecordStepFailedAsync(SdlcStage.Design, nameof(DesignStep), ex, ct);
            throw;
        }
        finally
        {
            sw.Stop();
            SdlcTelemetry.StageDuration.Record(sw.ElapsedMilliseconds, new KeyValuePair<string, object?>[] { new("sdlc.stage", "Design") });
        }
    }
}
