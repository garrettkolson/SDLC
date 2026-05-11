using FluentAssertions;
using SDLC.Contracts;
using NUnit.Framework;

namespace SDLC.Infrastructure.Tests;

[TestFixture, SingleThreaded]
public class StageGateStoreTests
{
    private StageGateStore _store = null!;
    private string _dbPath = null!;

    [SetUp]
    public async Task SetUp()
    {
        _dbPath = Path.GetTempFileName();
        _store = new StageGateStore(new SqlDbConnectionFactory($"Data Source={_dbPath}"));
        await _store.InitializeAsync();
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            if (File.Exists(_dbPath))
                File.Delete(_dbPath);
        }
        catch { }
    }

    [Test]
    public async Task CreateGateAsync_ReturnsGateWithNewId()
    {
        var artifact = new ResearchBrief { RunId = Guid.NewGuid(), Stage = SdlcStage.Research };
        var gate = await _store.CreateGateAsync(artifact);
        gate.GateId.Should().NotBe(Guid.Empty);
    }

    [Test]
    public async Task CreateGateAsync_Status_IsPending()
    {
        var artifact = new ResearchBrief { RunId = Guid.NewGuid(), Stage = SdlcStage.Research };
        var gate = await _store.CreateGateAsync(artifact);
        gate.Status.Should().Be(GateStatus.Pending);
    }

    [Test]
    public async Task GetAsync_AfterCreate_ReturnsGate()
    {
        var artifact = new ResearchBrief { RunId = Guid.NewGuid(), Stage = SdlcStage.Research };
        var gate = await _store.CreateGateAsync(artifact);

        var retrieved = await _store.GetAsync(gate.GateId);
        retrieved.Should().NotBeNull();
        retrieved!.GateId.Should().Be(gate.GateId);
    }

    [Test]
    public async Task ResolveAsync_Approve_UpdatesStatusAndTimestamp()
    {
        var artifact = new ResearchBrief { RunId = Guid.NewGuid(), Stage = SdlcStage.Research };
        var gate = await _store.CreateGateAsync(artifact);

        await _store.ResolveAsync(gate.GateId, GateDecision.Approved, notes: null, "system", "system");

        var retrieved = await _store.GetAsync(gate.GateId);
        retrieved!.Status.Should().Be(GateStatus.Approved);
        retrieved.ResolvedAt.Should().NotBeNull();
    }

    [Test]
    public async Task ResolveAsync_Reject_SetsRejectedStatus()
    {
        var artifact = new ResearchBrief { RunId = Guid.NewGuid(), Stage = SdlcStage.Research };
        var gate = await _store.CreateGateAsync(artifact);

        await _store.ResolveAsync(gate.GateId, GateDecision.Rejected, "Needs more detail", "user-1", "User One");

        var retrieved = await _store.GetAsync(gate.GateId);
        retrieved!.Status.Should().Be(GateStatus.Rejected);
        retrieved.Notes.Should().Be("Needs more detail");
    }

    [Test]
    public async Task GetPendingForRunAsync_ReturnsPendingGatesOnly()
    {
        var runId = Guid.NewGuid();
        var a1 = new ResearchBrief { RunId = runId, Stage = SdlcStage.Research };
        var a2 = new RequirementsSpec { RunId = runId, Stage = SdlcStage.Requirements };

        var g1 = await _store.CreateGateAsync(a1);
        var g2 = await _store.CreateGateAsync(a2);

        await _store.ResolveAsync(g1.GateId, GateDecision.Approved, null, "system", "system");

        var pending = await _store.GetPendingForRunAsync(runId);
        pending.Should().HaveCount(1);
        pending[0].GateId.Should().Be(g2.GateId);
    }
}
