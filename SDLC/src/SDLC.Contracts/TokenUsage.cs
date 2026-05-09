namespace SDLC.Contracts;

public record TokenUsage(long PromptTokens, long CompletionTokens)
{
    public long TotalTokens => PromptTokens + CompletionTokens;
    public static TokenUsage Zero => new(0, 0);
}
