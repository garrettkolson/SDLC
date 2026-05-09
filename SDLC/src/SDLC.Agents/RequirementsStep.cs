using System.Diagnostics;
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
        IRunBudgetTracker budgetTracker,
        CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        using var activity = telemetry.StartStageActivity(config.RunId, SdlcStage.Requirements);
        try
        {
            var kernel = kernelFactory.CreateForStage(SdlcStage.Requirements);
            var history = new List<string> { RequirementsPrompts.BuildPrompt(config.ProjectBrief, research.Content) };

            RequirementsSpec? spec = null;
            var lastAiResponse = "";

            for (var attempt = 0; attempt < MaxAttempts; attempt++)
            {
                await budgetTracker.EnsureWithinBudgetAsync(config.RunId, ct);

                var (response, usage) = await kernel.CompleteAsyncWithUsage(RequirementsPrompts.SystemPrompt, string.Join("\n", history), ct);
                lastAiResponse = response;
                history.Add($"AI: {response}");
                await budgetTracker.RecordAsync(config.RunId, usage.PromptTokens, usage.CompletionTokens, ct);
                await telemetry.RecordTokenUsageAsync(config.RunId, usage.PromptTokens, usage.CompletionTokens, ct);

                var (critiqueResponse, critiqueUsage) = await kernel.CompleteAsyncWithUsage(RequirementsPrompts.CritiquePrompt, response, ct);
                if (RequirementsPrompts.IsSatisfactory(critiqueResponse))
                {
                    spec = new RequirementsSpec { Content = response, RunId = config.RunId, Stage = SdlcStage.Requirements };
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

            spec ??= new RequirementsSpec { Content = lastAiResponse, RunId = config.RunId, Stage = SdlcStage.Requirements };

            await artifacts.SaveAsync(spec);
            await context.EmitEventAsync(new KernelProcessEvent { Id = SdlcEvents.RequirementsComplete, Data = spec }, ct);
            await telemetry.RecordStepCompletedAsync(SdlcStage.Requirements, nameof(RequirementsStep), ct);
            activity?.SetStatus(ActivityStatusCode.Ok);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.AddTag("error.type", ex.GetType().Name);
            activity?.AddTag("error.message", ex.Message);
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
