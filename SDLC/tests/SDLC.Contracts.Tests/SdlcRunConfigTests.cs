using FluentAssertions;
using SDLC.Contracts;
using NUnit.Framework;

namespace SDLC.Contracts.Tests;

public class SdlcRunConfigTests
{
    [Test]
    public void SdlcRunConfig_WhenCreated_HasNewRunId()
    {
        var config = new SdlcRunConfig { ProjectBrief = "Build a thing" };
        config.RunId.Should().NotBe(Guid.Empty);
    }

    [Test]
    public void SdlcRunConfig_TwoInstances_HaveDistinctRunIds()
    {
        var c1 = new SdlcRunConfig { ProjectBrief = "A" };
        var c2 = new SdlcRunConfig { ProjectBrief = "B" };
        c1.RunId.Should().NotBe(c2.RunId);
    }

    [Test]
    public void SdlcRunConfig_ModelRouting_DefaultsToNonNullDictionary()
    {
        var config = new SdlcRunConfig { ProjectBrief = "Test" };
        config.ModelRouting.Should().NotBeNull();
    }

    [TestCase(SdlcStage.Research)]
    [TestCase(SdlcStage.Requirements)]
    [TestCase(SdlcStage.Design)]
    [TestCase(SdlcStage.Build)]
    [TestCase(SdlcStage.Learn)]
    public void SdlcRunConfig_DefaultModelRouting_HasEntryForEveryStage(SdlcStage stage)
    {
        var config = new SdlcRunConfig { ProjectBrief = "Test" };
        config.ModelRouting.StageEndpoints.Should().ContainKey(stage);
    }
}
