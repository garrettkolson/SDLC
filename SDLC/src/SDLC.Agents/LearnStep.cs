using System.Diagnostics;
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
        IRunBudgetTracker budgetTracker,
        CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        using var activity = telemetry.StartStageActivity(config.RunId, SdlcStage.Learn);
        try
        {
            var kernel = kernelFactory.CreateForStage(SdlcStage.Learn);
            var history = new List<string> { LearnPrompts.BuildPrompt(config.ProjectBrief, buildResult.Logs, spec.Content) };

            LearnReport? report = null;
            var lastAiResponse = "";

            for (var attempt = 0; attempt < MaxAttempts; attempt++)
            {
                await budgetTracker.EnsureWithinBudgetAsync(config.RunId, ct);

                var (response, usage) = await kernel.CompleteAsyncWithUsage(LearnPrompts.SystemPrompt, string.Join("\n", history), ct);
                lastAiResponse = response;
                history.Add($"AI: {response}");
                await budgetTracker.RecordAsync(config.RunId, usage.PromptTokens, usage.CompletionTokens, ct);
                await telemetry.RecordTokenUsageAsync(config.RunId, usage.PromptTokens, usage.CompletionTokens, ct);

                var (critiqueResponse, critiqueUsage) = await kernel.CompleteAsyncWithUsage(LearnPrompts.CritiquePrompt, response, ct);
                if (LearnPrompts.IsSatisfactory(critiqueResponse))
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
                history.Add($"Critique: {critiqueResponse}");
                await budgetTracker.RecordAsync(config.RunId, critiqueUsage.PromptTokens, critiqueUsage.CompletionTokens, ct);
                await telemetry.RecordTokenUsageAsync(config.RunId, critiqueUsage.PromptTokens, critiqueUsage.CompletionTokens, ct);

                if (await budgetTracker.IsOverBudgetAsync(config.RunId, ct))
                {
                    history = HistoryTruncator.Apply(history);
                }
            }

            report ??= new LearnReport { Content = lastAiResponse, Retrospective = lastAiResponse, RunId = config.RunId, Stage = SdlcStage.Learn };

            await artifacts.SaveAsync(report);
            await context.EmitEventAsync(new KernelProcessEvent { Id = SdlcEvents.LearnComplete, Data = report }, ct);
            await telemetry.RecordStepCompletedAsync(SdlcStage.Learn, nameof(LearnStep), ct);
            activity?.SetStatus(ActivityStatusCode.Ok);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.AddTag("error.type", ex.GetType().Name);
            activity?.AddTag("error.message", ex.Message);
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
