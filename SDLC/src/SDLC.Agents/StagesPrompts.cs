using SDLC.Contracts;

namespace SDLC.Agents;

public static class ResearchPrompts
{
    public const string SystemPrompt = """
        You are a research agent. Analyze the project brief and produce a comprehensive research brief.
        Output the brief as markdown, then append [SATISFACTORY] or [UNSATISFACTORY] marker.
        """;

    public const string CritiquePrompt = """
        Review the research output above. Is it comprehensive and accurate?
        Respond with [SATISFACTORY] if good, or [UNSATISFACTORY] with feedback.
        """;

    public static string BuildPrompt(SdlcRunConfig config) =>
        $"Research this project: {config.ProjectBrief}";

    public static bool IsSatisfactory(string response) =>
        response.Contains("[SATISFACTORY]") && !response.Contains("[UNSATISFACTORY]");

    public static ResearchBrief ParseBrief(string content, Guid runId) =>
        new() { Content = content, RunId = runId, Stage = SdlcStage.Research };
}

public static class RequirementsPrompts
{
    public static readonly string SystemPrompt = """
        You are a requirements agent. Write a detailed requirements specification with acceptance criteria.
        Output as markdown, then append [SATISFACTORY] or [UNSATISFACTORY].
        """;

    public const string CritiquePrompt = """
        Review the requirements spec. Is it complete with clear acceptance criteria?
        Respond with [SATISFACTORY] or [UNSATISFACTORY] with feedback.
        """;

    public static string BuildPrompt(string projectBrief, string researchBrief) =>
        $"Write requirements for: {projectBrief}\n\nResearch context:\n{researchBrief}";

    public static bool IsSatisfactory(string response) =>
        response.Contains("[SATISFACTORY]") && !response.Contains("[UNSATISFACTORY]");
}

public static class DesignPrompts
{
    public static readonly string SystemPrompt = """
        You are an architecture agent. Design a system architecture with a Mermaid diagram.
        Output as markdown with a ```mermaid block, then append [SATISFACTORY] or [UNSATISFACTORY].
        """;

    public const string CritiquePrompt = """
        Review the architecture. Is it sound and well-documented?
        Respond with [SATISFACTORY] or [UNSATISFACTORY] with feedback.
        """;

    public static string BuildPrompt(string projectBrief, string researchBrief, string requirements) =>
        $"Design architecture for: {projectBrief}\n\nResearch:\n{researchBrief}\n\nRequirements:\n{requirements}";

    public static bool IsSatisfactory(string response) =>
        response.Contains("[SATISFACTORY]") && !response.Contains("[UNSATISFACTORY]");
}

public static class LearnPrompts
{
    public static readonly string SystemPrompt = """
        You are a learning agent. Write a retrospective on the build outcome.
        Include what went well, what didn't, and feedback items.
        Output as markdown, then append [SATISFACTORY] or [UNSATISFACTORY].
        """;

    public const string CritiquePrompt = """
        Review the retrospective. Is it honest and actionable?
        Respond with [SATISFACTORY] or [UNSATISFACTORY] with feedback.
        """;

    public static string BuildPrompt(string projectBrief, string buildLogs, string requirements) =>
        $"Write retrospective for: {projectBrief}\n\nBuild logs:\n{buildLogs}\n\nRequirements:\n{requirements}";

    public static bool IsSatisfactory(string response) =>
        response.Contains("[SATISFACTORY]") && !response.Contains("[UNSATISFACTORY]");
}
