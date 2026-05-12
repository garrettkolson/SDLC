using FluentAssertions;
using NUnit.Framework;
using SDLC.Dashboard.Hubs;

namespace SDLC.Dashboard.Tests;

[TestFixture]
public class RunStateHubTests
{
    [Test]
    public void RunStateHub_InheritsFromHub()
    {
        var hubType = typeof(RunStateHub);
        hubType.BaseType.Should().NotBeNull();
        hubType.BaseType!.Name.Should().Contain("Hub");
    }

    [Test]
    public void SubscribeToRun_Exists_ReturnsTask()
    {
        var method = typeof(RunStateHub).GetMethod("SubscribeToRun");
        method.Should().NotBeNull();
        method!.ReturnType.Should().Be(typeof(Task));
    }

    [Test]
    public void UnsubscribeFromRun_Exists_ReturnsTask()
    {
        var method = typeof(RunStateHub).GetMethod("UnsubscribeFromRun");
        method.Should().NotBeNull();
        method!.ReturnType.Should().Be(typeof(Task));
    }

    [Test]
    public void RunId_ConvertsToGuidString_Deterministically()
    {
        var runId = Guid.NewGuid();
        runId.ToString().Should().NotBeNullOrEmpty();

        // Same runId always produces same string
        var runId2 = runId;
        runId2.ToString().Should().Be(runId.ToString());
    }
}
