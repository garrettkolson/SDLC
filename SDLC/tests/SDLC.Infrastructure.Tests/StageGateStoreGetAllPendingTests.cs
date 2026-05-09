using FluentAssertions;
using NUnit.Framework;
using SDLC.Contracts;
using SDLC.Infrastructure;

namespace SDLC.Infrastructure.Tests;

[TestFixture, SingleThreaded]
public class StageGateStoreGetAllPendingTests
{
    private string _tempFile = null!;
    private StageGateStore _store = null!;

    [SetUp]
    public async Task SetUp()
    {
        _tempFile = Path.Combine(Path.GetTempPath(), $"test_gates_{Guid.NewGuid()}.db");
        var connString = $"Data Source={_tempFile}";
        _store = new StageGateStore(connString);
        await _store.InitializeAsync();
    }

    [TearDown]
    public void Cleanup()
    {
        try { File.Delete(_tempFile); } catch { /* ignore */ }
    }

    private StageGateStore GetStore() => new StageGateStore($"Data Source={_tempFile}");

    [Test]
    public async Task GetAllPendingAsync_ReturnsOnlyPending()
    {
        var research = new ResearchBrief { Content = "Test research", CreatedAt = DateTimeOffset.UtcNow };
        var requirements = new RequirementsSpec { Content = "Test requirements", CreatedAt = DateTimeOffset.UtcNow };

        var gate1 = await GetStore().CreateGateAsync(research);
        await GetStore().ResolveAsync(gate1.GateId, GateDecision.Approved, "OK", "user1", "User One");

        var gate2 = await GetStore().CreateGateAsync(requirements);

        var pending = await GetStore().GetAllPendingAsync();

        pending.Should().HaveCount(1);
        pending[0].GateId.Should().Be(gate2.GateId);
    }

    [Test]
    public async Task GetAllPendingAsync_ReturnsAllPendingAcrossRuns()
    {
        var research1 = new ResearchBrief { Content = "R1", CreatedAt = DateTimeOffset.UtcNow };
        var research2 = new ResearchBrief { Content = "R2", CreatedAt = DateTimeOffset.UtcNow };

        await GetStore().CreateGateAsync(research1);
        await GetStore().CreateGateAsync(research2);

        var pending = await GetStore().GetAllPendingAsync();

        pending.Should().HaveCount(2);
    }
}
