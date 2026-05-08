using System.Collections.Concurrent;
using SDLC.Contracts;

namespace SDLC.Infrastructure;

public class RunBudgetTracker : IRunBudgetTracker
{
    private readonly long _budgetLimit;
    private readonly ConcurrentDictionary<Guid, TokenAccumulator> _usage = new();

    public RunBudgetTracker(long budgetLimit)
    {
        _budgetLimit = budgetLimit;
    }

    public long BudgetLimit => _budgetLimit;

    public Task RecordAsync(Guid runId, long promptTokens, long completionTokens, CancellationToken ct = default)
    {
        var acc = _usage.GetOrAdd(runId, _ => new TokenAccumulator());
        acc.Add(promptTokens, completionTokens);
        return Task.CompletedTask;
    }

    public Task<bool> IsOverBudgetAsync(Guid runId, CancellationToken ct = default)
    {
        return Task.FromResult(_usage.TryGetValue(runId, out var a) && a.Total > _budgetLimit);
    }

    public Task EnsureWithinBudgetAsync(Guid runId, CancellationToken ct = default)
    {
        if (_usage.TryGetValue(runId, out var acc) && acc.Total > _budgetLimit)
            throw new BudgetExceededException(acc.PromptTokens, acc.CompletionTokens, _budgetLimit);
        return Task.CompletedTask;
    }

    public Task<TokenUsage> GetUsageAsync(Guid runId, CancellationToken ct = default)
    {
        return Task.FromResult(_usage.TryGetValue(runId, out var a)
            ? new TokenUsage(a.PromptTokens, a.CompletionTokens)
            : TokenUsage.Zero);
    }

    private class TokenAccumulator
    {
        public long PromptTokens { get; private set; }
        public long CompletionTokens { get; private set; }
        public long Total => PromptTokens + CompletionTokens;

        public void Add(long prompt, long completion)
        {
            PromptTokens += prompt;
            CompletionTokens += completion;
        }
    }
}
