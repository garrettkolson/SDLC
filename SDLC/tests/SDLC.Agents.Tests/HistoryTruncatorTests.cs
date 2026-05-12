using FluentAssertions;
using NUnit.Framework;
using SDLC.Agents;

namespace SDLC.Agents.Tests;

[TestFixture, SingleThreaded]
public class HistoryTruncatorTests
{
    [Test]
    public void Apply_ReturnsOriginal_WhenUnderLimit()
    {
        var history = new List<string> { "system", "AI: response1", "Critique: fine" };
        var result = HistoryTruncator.Apply(history, maxTurns: 10);
        result.Should().HaveCount(3);
        result.Should().BeEquivalentTo(history);
    }

    [Test]
    public void Apply_KeepsSystemPromptAndLastNTurns()
    {
        var history = new List<string>
        {
            "system prompt",
            "AI: r1", "Critique: c1",
            "AI: r2", "Critique: c2",
            "AI: r3", "Critique: c3",
            "AI: r4", "Critique: c4",
        };
        var result = HistoryTruncator.Apply(history, maxTurns: 2);
        result.Should().HaveCount(5);
        result[0].Should().Be("system prompt");
        result[1].Should().Be("AI: r3");
        result[2].Should().Be("Critique: c3");
        result[3].Should().Be("AI: r4");
        result[4].Should().Be("Critique: c4");
    }

    [Test]
    public void Apply_DoesNotModifyOriginalList()
    {
        var history = new List<string> { "system", "AI: r1", "Critique: c1" };
        HistoryTruncator.Apply(history, maxTurns: 1);
        history.Should().HaveCount(3);
    }

    [Test]
    public void Apply_EmptyInput_ReturnsEmpty()
    {
        var history = new List<string>();
        var result = HistoryTruncator.Apply(history, maxTurns: 10);
        result.Should().BeEmpty();
    }

    [Test]
    public void Apply_SingleElement_ReturnsSingleElement()
    {
        var history = new List<string> { "system" };
        var result = HistoryTruncator.Apply(history, maxTurns: 10);
        result.Should().ContainSingle("system");
    }

    [Test]
    public void Apply_LargeInput_TruncatesCorrectly()
    {
        var history = new List<string> { "system" };
        for (int i = 0; i < 100; i++)
            history.Add($"msg{i}");

        var result = HistoryTruncator.Apply(history, maxTurns: 10);

        result.Count.Should().Be(21);
        result[0].Should().Be("system");
        result.Last().Should().Be("msg99");
    }

    [Test]
    public void Apply_ExactlyAtLimit_ReturnsUnchanged()
    {
        var history = new List<string> { "system", "m1", "m2" };
        var result = HistoryTruncator.Apply(history, maxTurns: 2);
        result.Should().BeEquivalentTo(history);
    }
}
