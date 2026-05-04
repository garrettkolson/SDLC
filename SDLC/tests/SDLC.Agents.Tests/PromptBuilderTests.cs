using FluentAssertions;
using NUnit.Framework;
using SDLC.Agents;
using SDLC.Contracts;

namespace SDLC.Agents.Tests;

[TestFixture, SingleThreaded]
public class PromptBuilderTests
{
    [Test]
    public void ResearchPrompts_BuildPrompt_ContainsProjectBrief()
    {
        var config = new SdlcRunConfig { ProjectBrief = "Build an invoice system" };
        var prompt = ResearchPrompts.BuildPrompt(config);
        prompt.Should().Contain("Build an invoice system");
    }

    [Test]
    public void ResearchPrompts_CritiquePrompt_IsNonEmpty()
    {
        ResearchPrompts.CritiquePrompt.Should().NotBeNullOrWhiteSpace();
    }

    [Test]
    public void ResearchPrompts_IsSatisfactory_TrueWhenMarkerPresent()
    {
        ResearchPrompts.IsSatisfactory("Good output. [SATISFACTORY]").Should().BeTrue();
    }

    [Test]
    public void ResearchPrompts_IsSatisfactory_FalseWhenOnlyUnsatisfactoryMarkerPresent()
    {
        ResearchPrompts.IsSatisfactory("Needs more work. [UNSATISFACTORY]").Should().BeFalse();
    }

    [Test]
    public void ResearchPrompts_IsSatisfactory_FalseWhenNeitherMarkerPresent()
    {
        ResearchPrompts.IsSatisfactory("Needs more work.").Should().BeFalse();
    }

    [TestCase(typeof(RequirementsPrompts))]
    [TestCase(typeof(DesignPrompts))]
    [TestCase(typeof(LearnPrompts))]
    [TestCase(typeof(ResearchPrompts))]
    public void AllPromptClasses_HaveNonEmptySystemPrompt(Type promptClass)
    {
        // const becomes a field, static readonly becomes a field too
        var field = promptClass.GetField("SystemPrompt",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static |
            System.Reflection.BindingFlags.FlattenHierarchy);

        field.Should().NotBeNull($"SystemPrompt must exist on {promptClass.Name}");

        var value = field!.GetValue(null);
        value.Should().NotBeNull();
        ((string)value!).Should().NotBeNullOrWhiteSpace();
    }
}
