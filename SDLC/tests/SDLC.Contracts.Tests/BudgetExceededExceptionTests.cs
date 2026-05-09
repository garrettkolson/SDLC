using FluentAssertions;
using NUnit.Framework;
using SDLC.Contracts;

namespace SDLC.Contracts.Tests;

[TestFixture, SingleThreaded]
public class BudgetExceededExceptionTests
{
    [Test]
    public void Constructor_SetsProperties()
    {
        var ex = new BudgetExceededException(100, 200, 500);
        ex.PromptTokens.Should().Be(100);
        ex.CompletionTokens.Should().Be(200);
        ex.BudgetLimit.Should().Be(500);
    }

    [Test]
    public void Constructor_MessageContainsTokenInfo()
    {
        var ex = new BudgetExceededException(100, 200, 500);
        ex.Message.Should().Contain("100 prompt");
        ex.Message.Should().Contain("200 completion");
        ex.Message.Should().Contain("500");
    }
}
