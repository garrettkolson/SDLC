using Microsoft.Extensions.Logging;
using SDLC.Contracts;
using SDLC.Infrastructure;
using SDLC.Telemetry;

namespace SDLC.Agents;

public class ResearchStep
{
    private const int MaxAttempts = 3;

    public async Task RunAsync(
        IKernelProcessStepContext context,
        SdlcRunConfig config,
        IKernelFactory kernelFactory,
        IArtifactStore artifacts,
        IPipelineTelemetry? telemetry = null,
        CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var kernel = kernelFactory.CreateForStage(SdlcStage.Research);
            var history = new List<string> { ResearchPrompts.BuildPrompt(config) };

            ResearchBrief? brief = null;
            var lastAiResponse = "";

            for (var attempt = 0; attempt < MaxAttempts; attempt++)
            {
                var response = await kernel.CompleteAsync(ResearchPrompts.SystemPrompt, string.Join("\n", history), ct);
                lastAiResponse = response;
                history.Add($"AI: {response}");

                var critique = await kernel.CompleteAsync(ResearchPrompts.CritiquePrompt, response, ct);

                if (ResearchPrompts.IsSatisfactory(critique))
                {
                    brief = ResearchPrompts.ParseBrief(response, config.RunId);
                    break;
                }
                history.Add($"Critique: {critique}");
            }

            brief ??= new ResearchBrief { Content = lastAiResponse, RunId = config.RunId, Stage = SdlcStage.Research };
            await artifacts.SaveAsync(brief);
            await context.EmitEventAsync(new KernelProcessEvent
            {
                Id = SdlcEvents.ResearchComplete,
                Data = brief
            }, ct);
            if (telemetry != null)
                await telemetry.RecordStepCompletedAsync(SdlcStage.Research, nameof(ResearchStep), ct);
        }
        catch (Exception ex)
        {
            if (telemetry != null)
                await telemetry.RecordStepFailedAsync(SdlcStage.Research, nameof(ResearchStep), ex, ct);
            throw;
        }
        finally
        {
            sw.Stop();
            SdlcTelemetry.StageDuration.Record(sw.ElapsedMilliseconds, new KeyValuePair<string, object?>[] { new("sdlc.stage", "Research") });
        }
    }
}
