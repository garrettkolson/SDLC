using SDLC.Contracts;
using SDLC.Infrastructure;
using SDLC.Telemetry;

namespace SDLC.Agents;

public class LearnStep
{
    private const int MaxAttempts = 3;

    public async Task RunAsync(
        IKernelProcessStepContext context,
        SdlcRunConfig config,
        RequirementsSpec spec,
        BuildResult buildResult,
        IKernelFactory kernelFactory,
        IArtifactStore artifacts,
        IPipelineTelemetry telemetry,
        CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var kernel = kernelFactory.CreateForStage(SdlcStage.Learn);
            var history = new List<string> { LearnPrompts.BuildPrompt(config.ProjectBrief, buildResult.Logs, spec.Content) };

            LearnReport? report = null;
            var lastAiResponse = "";

            for (var attempt = 0; attempt < MaxAttempts; attempt++)
            {
                var response = await kernel.CompleteAsync(LearnPrompts.SystemPrompt, string.Join("\n", history), ct);
                lastAiResponse = response;
                history.Add($"AI: {response}");

                var critique = await kernel.CompleteAsync(LearnPrompts.CritiquePrompt, response, ct);

                if (LearnPrompts.IsSatisfactory(critique))
                {
                    report = new LearnReport
                    {
                        Content = response,
                        Retrospective = response,
                        RunId = config.RunId,
                        Stage = SdlcStage.Learn
                    };
                    break;
                }
                history.Add($"Critique: {critique}");
            }

            report ??= new LearnReport { Content = lastAiResponse, Retrospective = lastAiResponse, RunId = config.RunId, Stage = SdlcStage.Learn };

            await artifacts.SaveAsync(report);
            await context.EmitEventAsync(new KernelProcessEvent { Id = SdlcEvents.LearnComplete, Data = report }, ct);
            await telemetry.RecordStepCompletedAsync(SdlcStage.Learn, nameof(LearnStep), ct);
        }
        catch (Exception ex)
        {
            await telemetry.RecordStepFailedAsync(SdlcStage.Learn, nameof(LearnStep), ex, ct);
            throw;
        }
        finally
        {
            sw.Stop();
            SdlcTelemetry.StageDuration.Record(sw.ElapsedMilliseconds, new KeyValuePair<string, object?>[] { new("sdlc.stage", "Learn") });
        }
    }
}
