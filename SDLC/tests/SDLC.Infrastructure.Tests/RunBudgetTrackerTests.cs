using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using SDLC.Contracts;
using SDLC.Infrastructure;

namespace SDLC.Infrastructure.Tests;

[TestFixture, SingleThreaded]
public class RunBudgetTrackerTests
{
    private IRunBudgetTracker _tracker = null!;

    [SetUp]
    public void SetUp()
    {
        _tracker = new RunBudgetTracker(1000);
    }

    [Test]
    public async Task RecordAsync_AccumulatesTokens()
    {
        var runId = Guid.NewGuid();
        await _tracker.RecordAsync(runId, 100, 50);
        await _tracker.RecordAsync(runId, 200, 100);

        var usage = await _tracker.GetUsageAsync(runId);
        usage.PromptTokens.Should().Be(300);
        usage.CompletionTokens.Should().Be(150);
    }

    [Test]
    public async Task IsOverBudgetAsync_ReturnsFalse_UnderBudget()
    {
        var runId = Guid.NewGuid();
        await _tracker.RecordAsync(runId, 100, 100);
        (await _tracker.IsOverBudgetAsync(runId)).Should().BeFalse();
    }

    [Test]
    public async Task IsOverBudgetAsync_ReturnsTrue_OverBudget()
    {
        var runId = Guid.NewGuid();
        await _tracker.RecordAsync(runId, 600, 500);
        (await _tracker.IsOverBudgetAsync(runId)).Should().BeTrue();
    }

    [Test]
    public async Task EnsureWithinBudgetAsync_DoesNotThrow_WhenUnderBudget()
    {
        var runId = Guid.NewGuid();
        await _tracker.RecordAsync(runId, 100, 100);
        await _tracker.EnsureWithinBudgetAsync(runId);
    }

    [Test]
    public async Task EnsureWithinBudgetAsync_ThrowsBudgetExceededException_WhenOverBudget()
    {
        var runId = Guid.NewGuid();
        await _tracker.RecordAsync(runId, 600, 500);

        var act = async () => await _tracker.EnsureWithinBudgetAsync(runId);
        await act.Should().ThrowAsync<BudgetExceededException>();
    }

    [Test]
    public async Task EnsureWithinBudgetAsync_ExceptionContainsCorrectValues()
    {
        var runId = Guid.NewGuid();
        await _tracker.RecordAsync(runId, 600, 500);

        BudgetExceededException? ex = null;
        try { await _tracker.EnsureWithinBudgetAsync(runId); }
        catch (BudgetExceededException e) { ex = e; }

        ex!.PromptTokens.Should().Be(600);
        ex.CompletionTokens.Should().Be(500);
        ex.BudgetLimit.Should().Be(1000);
    }

    [Test]
    public async Task GetUsageAsync_ReturnsZero_ForUnknownRun()
    {
        var usage = await _tracker.GetUsageAsync(Guid.NewGuid());
        usage.Should().Be(TokenUsage.Zero);
    }

    [Test]
    public async Task IsolatedTracking_DifferentRunsDoNotInterfere()
    {
        var runA = Guid.NewGuid();
        var runB = Guid.NewGuid();

        await _tracker.RecordAsync(runA, 900, 50);
        await _tracker.RecordAsync(runB, 100, 50);

        (await _tracker.IsOverBudgetAsync(runA)).Should().BeFalse();
        (await _tracker.IsOverBudgetAsync(runB)).Should().BeFalse();
    }

    [Test]
    public void BudgetLimit_ReturnsConfiguredValue()
    {
        _tracker.BudgetLimit.Should().Be(1000);
    }
}
