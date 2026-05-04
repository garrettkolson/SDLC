using FluentAssertions;
using SDLC.Contracts;
using NUnit.Framework;

namespace SDLC.Contracts.Tests;

public class ArtifactTests
{
    [Test]
    public void SdlcArtifact_WhenCreated_HasNewArtifactId()
    {
        var artifact = new ResearchBrief { RunId = Guid.NewGuid(), Stage = SdlcStage.Research };
        artifact.ArtifactId.Should().NotBe(Guid.Empty);
    }

    [Test]
    public void SdlcArtifact_WhenCreated_HasUtcCreatedAt()
    {
        var before = DateTimeOffset.UtcNow;
        var artifact = new ResearchBrief { RunId = Guid.NewGuid(), Stage = SdlcStage.Research };
        var after = DateTimeOffset.UtcNow;

        artifact.CreatedAt.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);
        artifact.CreatedAt.Offset.Should().Be(TimeSpan.Zero);
    }

    [Test]
    public void SdlcArtifact_DefaultStatus_IsDraft()
    {
        var artifact = new ResearchBrief { RunId = Guid.NewGuid(), Stage = SdlcStage.Research };
        artifact.Status.Should().Be(ArtifactStatus.Draft);
    }

    [Test]
    public void SdlcArtifact_TwoInstances_HaveDistinctArtifactIds()
    {
        var a1 = new ResearchBrief { RunId = Guid.NewGuid(), Stage = SdlcStage.Research };
        var a2 = new ResearchBrief { RunId = Guid.NewGuid(), Stage = SdlcStage.Research };
        a1.ArtifactId.Should().NotBe(a2.ArtifactId);
    }

    [TestCase(ArtifactStatus.Draft)]
    [TestCase(ArtifactStatus.PendingReview)]
    [TestCase(ArtifactStatus.Approved)]
    [TestCase(ArtifactStatus.Rejected)]
    public void ArtifactStatus_AllValuesAreDefined(ArtifactStatus status)
    {
        Enum.IsDefined(status).Should().BeTrue();
    }
}
