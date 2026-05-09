namespace SDLC.Contracts;

public class BudgetExceededException : Exception
{
    public long PromptTokens { get; }
    public long CompletionTokens { get; }
    public long BudgetLimit { get; }

    public BudgetExceededException(long promptTokens, long completionTokens, long budgetLimit)
        : base($"Token budget exceeded: {promptTokens} prompt + {completionTokens} completion > {budgetLimit}.")
    {
        PromptTokens = promptTokens;
        CompletionTokens = completionTokens;
        BudgetLimit = budgetLimit;
    }
}
