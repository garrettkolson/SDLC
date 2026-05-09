using System.Diagnostics;
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
        IPipelineTelemetry telemetry,
        IRunBudgetTracker budgetTracker,
        CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        using var activity = telemetry.StartStageActivity(config.RunId, SdlcStage.Design);
        try
        {
            var kernel = kernelFactory.CreateForStage(SdlcStage.Design);
            var history = new List<string> { DesignPrompts.BuildPrompt(config.ProjectBrief, research.Content, spec.Content) };

            ArchitectureRecord? record = null;
            var lastAiResponse = "";

            for (var attempt = 0; attempt < MaxAttempts; attempt++)
            {
                await budgetTracker.EnsureWithinBudgetAsync(config.RunId, ct);

                var (response, usage) = await kernel.CompleteAsyncWithUsage(DesignPrompts.SystemPrompt, string.Join("\n", history), ct);
                lastAiResponse = response;
                history.Add($"AI: {response}");
                await budgetTracker.RecordAsync(config.RunId, usage.PromptTokens, usage.CompletionTokens, ct);
                await telemetry.RecordTokenUsageAsync(config.RunId, usage.PromptTokens, usage.CompletionTokens, ct);

                var (critiqueResponse, critiqueUsage) = await kernel.CompleteAsyncWithUsage(DesignPrompts.CritiquePrompt, response, ct);
                if (DesignPrompts.IsSatisfactory(critiqueResponse))
                {
                    record = new ArchitectureRecord { Content = response, RunId = config.RunId, Stage = SdlcStage.Design };
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

            record ??= new ArchitectureRecord { Content = lastAiResponse, RunId = config.RunId, Stage = SdlcStage.Design };

            await artifacts.SaveAsync(record);
            await context.EmitEventAsync(new KernelProcessEvent { Id = SdlcEvents.DesignComplete, Data = record }, ct);
            await telemetry.RecordStepCompletedAsync(SdlcStage.Design, nameof(DesignStep), ct);
            activity?.SetStatus(ActivityStatusCode.Ok);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.AddTag("error.type", ex.GetType().Name);
            activity?.AddTag("error.message", ex.Message);
            await telemetry.RecordStepFailedAsync(SdlcStage.Design, nameof(DesignStep), ex, ct);
            throw;
        }
        finally
        {
            sw.Stop();
            SdlcTelemetry.StageDuration.Record(sw.ElapsedMilliseconds,
                new KeyValuePair<string, object?>[] { new("sdlc.stage", "Design") });
        }
    }
}
