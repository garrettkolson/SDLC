using SDLC.Contracts;
using SDLC.Infrastructure;
using SDLC.Telemetry;

namespace SDLC.Agents;

public class RequirementsStep
{
    private const int MaxAttempts = 3;

    public async Task RunAsync(
        IKernelProcessStepContext context,
        SdlcRunConfig config,
        ResearchBrief research,
        IKernelFactory kernelFactory,
        IArtifactStore artifacts,
        IPipelineTelemetry telemetry,
        CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var kernel = kernelFactory.CreateForStage(SdlcStage.Requirements);
            var history = new List<string> { RequirementsPrompts.BuildPrompt(config.ProjectBrief, research.Content) };

            RequirementsSpec? spec = null;
            var lastAiResponse = "";

            for (var attempt = 0; attempt < MaxAttempts; attempt++)
            {
                var response = await kernel.CompleteAsync(RequirementsPrompts.SystemPrompt, string.Join("\n", history), ct);
                lastAiResponse = response;
                history.Add($"AI: {response}");

                var critique = await kernel.CompleteAsync(RequirementsPrompts.CritiquePrompt, response, ct);

                if (RequirementsPrompts.IsSatisfactory(critique))
                {
                    spec = new RequirementsSpec { Content = response, RunId = config.RunId, Stage = SdlcStage.Requirements };
                    break;
                }
                history.Add($"Critique: {critique}");
            }

            spec ??= new RequirementsSpec { Content = lastAiResponse, RunId = config.RunId, Stage = SdlcStage.Requirements };

            await artifacts.SaveAsync(spec);
            await context.EmitEventAsync(new KernelProcessEvent { Id = SdlcEvents.RequirementsComplete, Data = spec }, ct);
            await telemetry.RecordStepCompletedAsync(SdlcStage.Requirements, nameof(RequirementsStep), ct);
        }
        catch (Exception ex)
        {
            await telemetry.RecordStepFailedAsync(SdlcStage.Requirements, nameof(RequirementsStep), ex, ct);
            throw;
        }
        finally
        {
            sw.Stop();
            SdlcTelemetry.StageDuration.Record(sw.ElapsedMilliseconds, 
                new KeyValuePair<string, object?>[] { new("sdlc.stage", "Requirements") });
        }
    }
}
