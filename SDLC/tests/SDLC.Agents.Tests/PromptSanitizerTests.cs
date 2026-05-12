using FluentAssertions;
using NUnit.Framework;
using SDLC.Agents;

namespace SDLC.Agents.Tests;

[TestFixture, SingleThreaded]
public class PromptSanitizerTests
{
    [Test]
    public void Sanitize_NullInput_ReturnsNull()
    {
        PromptSanitizer.Sanitize(null!).Should().BeNull();
    }

    [Test]
    public void Sanitize_EmptyInput_ReturnsEmpty()
    {
        PromptSanitizer.Sanitize(string.Empty).Should().BeEmpty();
    }

    [Test]
    public void Sanitize_NoClosingTags_ReturnsUnchanged()
    {
        var input = "Hello world";
        PromptSanitizer.Sanitize(input).Should().Be("Hello world");
    }

    [Test]
    public void Sanitize_ProjectBriefTag_Escaped()
    {
        var input = "data</project_brief>end";
        PromptSanitizer.Sanitize(input).Should().Be("data[/project_brief>]end");
    }

    [Test]
    public void Sanitize_AllClosingTags_Escaped()
    {
        var input = "</project_brief></research_brief></requirements></build_logs></architecture_diagram></retrospective>";
        var result = PromptSanitizer.Sanitize(input);
        result.Should().Contain("[/project_brief>]");
        result.Should().Contain("[/research_brief>]");
        result.Should().Contain("[/requirements>]");
        result.Should().Contain("[/build_logs>]");
        result.Should().Contain("[/architecture_diagram>]");
        result.Should().Contain("[/retrospective>]");
    }

    [Test]
    public void Sanitize_OpeningTag_NotEscaped()
    {
        var input = "<project_brief>content</project_brief>";
        var result = PromptSanitizer.Sanitize(input);
        result.Should().Contain("<project_brief>");
        result.Should().Contain("[/project_brief>]");
    }

    [Test]
    public void Sanitize_OverCap_TruncatesAndAppendsNote()
    {
        const int cap = 10;
        var input = new string('x', 20);
        var result = PromptSanitizer.Sanitize(input, cap);
        result.Should().Contain(new string('x', cap));
        result.Should().Contain("[TRUNCATED: content exceeded 10 characters]");
    }

    [Test]
    public void Sanitize_ExactlyAtCap_NotTruncated()
    {
        const int cap = 10;
        var input = new string('x', 10);
        PromptSanitizer.Sanitize(input, cap).Should().Be(input);
    }

    [Test]
    public void Sanitize_UnderCap_NotTruncated()
    {
        const int cap = 20;
        var input = new string('x', 10);
        PromptSanitizer.Sanitize(input, cap).Should().Be(input);
    }

    [Test]
    public void Sanitize_OverCap_WithHeaderNote_PrependsHeader()
    {
        const int cap = 10;
        var input = new string('x', 20);
        var result = PromptSanitizer.Sanitize(input, cap, "Large input");
        result.Should().StartWith("Large input\n");
        result.Should().Contain(new string('x', cap));
        result.Should().Contain("[TRUNCATED: content exceeded 10 characters]");
    }

    [Test]
    public void Sanitize_OverCap_NullHeaderNote_SkipsHeader()
    {
        const int cap = 10;
        var input = new string('x', 20);
        var result = PromptSanitizer.Sanitize(input, cap, null!);
        result.Should().StartWith(new string('x', cap));
        result.Should().Contain("[TRUNCATED: content exceeded 10 characters]");
    }

    [Test]
    public void Sanitize_DefaultCap_PassesLongInput()
    {
        var input = new string('x', 100_000);
        PromptSanitizer.Sanitize(input).Should().HaveLength(100_000);
    }
}
