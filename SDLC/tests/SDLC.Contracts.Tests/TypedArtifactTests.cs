using FluentAssertions;
using SDLC.Contracts;
using NUnit.Framework;

namespace SDLC.Contracts.Tests;

public class TypedArtifactTests
{
    [Test]
    public void ResearchBrief_Content_DefaultsToEmpty()
    {
        var brief = new ResearchBrief();
        brief.Content.Should().BeEmpty();
    }

    [Test]
    public void RequirementsSpec_Criteria_DefaultsToEmptyList()
    {
        var spec = new RequirementsSpec();
        spec.Criteria.Should().NotBeNull().And.BeEmpty();
    }

    [Test]
    public void RequirementsSpec_CanHoldMultipleCriteria()
    {
        var spec = new RequirementsSpec
        {
            Criteria =
            [
                new AcceptanceCriterion { Id = "AC-1", Description = "Given X when Y then Z" },
                new AcceptanceCriterion { Id = "AC-2", Description = "Given A when B then C" }
            ]
        };
        spec.Criteria.Should().HaveCount(2);
    }

    [Test]
    public void ArchitectureRecord_DiagramMermaid_DefaultsToEmpty()
    {
        var record = new ArchitectureRecord();
        record.DiagramMermaid.Should().BeEmpty();
    }

    [Test]
    public void BuildResult_Success_DefaultsToFalse()
    {
        var result = new BuildResult();
        result.Success.Should().BeFalse();
    }

    [Test]
    public void BuildResult_SweAfRunId_DefaultsToEmpty()
    {
        var result = new BuildResult();
        result.SweAfRunId.Should().BeEmpty();
    }

    [Test]
    public void LearnReport_FeedbackItems_DefaultsToEmptyList()
    {
        var report = new LearnReport();
        report.FeedbackItems.Should().NotBeNull().And.BeEmpty();
    }
}
