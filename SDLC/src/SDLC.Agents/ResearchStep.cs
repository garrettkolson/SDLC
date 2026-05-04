using Microsoft.Extensions.Logging;
using SDLC.Contracts;
using SDLC.Infrastructure;

namespace SDLC.Agents;

public class ResearchStep
{
    public static int MaxAttempts = 3;

    public async Task RunAsync(
        IKernelProcessStepContext context,
        SdlcRunConfig config,
        IKernelFactory kernelFactory,
        IArtifactStore artifacts,
        CancellationToken ct = default)
    {
        var kernel = kernelFactory.CreateForStage(SdlcStage.Research);
        var history = new List<string>();
        history.Add(ResearchPrompts.BuildPrompt(config));

        ResearchBrief? brief = null;

        for (int attempt = 0; attempt < MaxAttempts; attempt++)
        {
            var response = await kernel.CompleteAsync(ResearchPrompts.SystemPrompt, string.Join("\n", history), ct);
            history.Add($"AI: {response}");

            var critique = await kernel.CompleteAsync(ResearchPrompts.CritiquePrompt, response, ct);

            if (ResearchPrompts.IsSatisfactory(critique))
            {
                brief = ResearchPrompts.ParseBrief(response, config.RunId);
                break;
            }
            history.Add($"Critique: {critique}");
        }

        if (brief is null)
        {
            var lastResponse = history[history.Count - 1];
            var text = lastResponse.StartsWith("AI: ") ? lastResponse.Substring(4) : lastResponse;
            brief = new ResearchBrief { Content = text, RunId = config.RunId, Stage = SdlcStage.Research };
        }
        await artifacts.SaveAsync(brief);
        await context.EmitEventAsync(new KernelProcessEvent
        {
            Id = SdlcEvents.ResearchComplete,
            Data = brief
        }, ct);
    }
}
