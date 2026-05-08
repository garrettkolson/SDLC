namespace SDLC.Contracts;

public interface IRunBudgetTracker
{
    Task RecordAsync(Guid runId, long promptTokens, long completionTokens, CancellationToken ct = default);
    Task<bool> IsOverBudgetAsync(Guid runId, CancellationToken ct = default);
    Task EnsureWithinBudgetAsync(Guid runId, CancellationToken ct = default);
    Task<TokenUsage> GetUsageAsync(Guid runId, CancellationToken ct = default);
    long BudgetLimit { get; }
}
