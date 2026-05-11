namespace SDLC.Agents;

public static class HistoryTruncator
{
    public static List<string> Apply(List<string> history, int maxTurns = 10)
    {
        if (history.Count <= maxTurns + 1)
            return history;

        if (history.Count <= maxTurns * 2 + 1)
            return history;

        var systemPrompt = history[0];
        var truncated = history.Skip(history.Count - maxTurns * 2).ToList();
        truncated.Insert(0, systemPrompt);
        return truncated;
    }
}
