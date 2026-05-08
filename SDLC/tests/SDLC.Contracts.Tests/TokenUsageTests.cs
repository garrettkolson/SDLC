using FluentAssertions;
using NUnit.Framework;
using SDLC.Contracts;

namespace SDLC.Contracts.Tests;

[TestFixture, SingleThreaded]
public class TokenUsageTests
{
    [Test]
    public void TotalTokens_SumOfPromptAndCompletion()
    {
        var usage = new TokenUsage(100, 200);
        usage.TotalTokens.Should().Be(300);
    }

    [Test]
    public void Zero_ReturnsZeroUsage()
    {
        TokenUsage.Zero.PromptTokens.Should().Be(0);
        TokenUsage.Zero.CompletionTokens.Should().Be(0);
        TokenUsage.Zero.TotalTokens.Should().Be(0);
    }
}
