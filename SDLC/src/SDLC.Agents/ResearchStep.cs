using System.Diagnostics;
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
        IPipelineTelemetry telemetry,
        IRunBudgetTracker budgetTracker,
        CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        using var activity = telemetry.StartStageActivity(config.RunId, SdlcStage.Research);
        try
        {
            var kernel = kernelFactory.CreateForStage(SdlcStage.Research);
            var history = new List<string> { ResearchPrompts.BuildPrompt(config) };

            ResearchBrief? brief = null;
            var lastAiResponse = "";

            for (var attempt = 0; attempt < MaxAttempts; attempt++)
            {
                await budgetTracker.EnsureWithinBudgetAsync(config.RunId, ct);

                var (response, usage) = await kernel.CompleteAsyncWithUsage(ResearchPrompts.SystemPrompt, string.Join("\n", history), ct);
                lastAiResponse = response;
                history.Add($"AI: {response}");
                await budgetTracker.RecordAsync(config.RunId, usage.PromptTokens, usage.CompletionTokens, ct);
                await telemetry.RecordTokenUsageAsync(config.RunId, usage.PromptTokens, usage.CompletionTokens, ct);

                var (critiqueResponse, critiqueUsage) = await kernel.CompleteAsyncWithUsage(ResearchPrompts.CritiquePrompt, response, ct);
                if (ResearchPrompts.IsSatisfactory(critiqueResponse))
                {
                    brief = ResearchPrompts.ParseBrief(response, config.RunId);
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

            brief ??= new ResearchBrief { Content = lastAiResponse, RunId = config.RunId, Stage = SdlcStage.Research };
            await artifacts.SaveAsync(brief);
            await context.EmitEventAsync(new KernelProcessEvent
            {
                Id = SdlcEvents.ResearchComplete,
                Data = brief
            }, ct);
            await telemetry.RecordStepCompletedAsync(SdlcStage.Research, nameof(ResearchStep), ct);
            activity?.SetStatus(ActivityStatusCode.Ok);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.AddTag("error.type", ex.GetType().Name);
            activity?.AddTag("error.message", ex.Message);
            await telemetry.RecordStepFailedAsync(SdlcStage.Research, nameof(ResearchStep), ex, ct);
            throw;
        }
        finally
        {
            sw.Stop();
            SdlcTelemetry.StageDuration.Record(sw.ElapsedMilliseconds,
                new KeyValuePair<string, object?>[] { new("sdlc.stage", "Research") });
        }
    }
}
