using FluentAssertions;
using NUnit.Framework;
using SDLC.Dashboard.Services;

namespace SDLC.Dashboard.Tests;

[TestFixture]
public class RateLimiterTests
{
    [Test]
    public void Allow_ReturnsTrue_WhenUnderLimit()
    {
        var limiter = new RateLimiter(3, TimeSpan.FromSeconds(1));
        for (int i = 0; i < 3; i++)
        {
            limiter.Allow("key1").Should().BeTrue();
        }
    }

    [Test]
    public void Allow_ReturnsFalse_WhenOverLimit()
    {
        var limiter = new RateLimiter(3, TimeSpan.FromSeconds(1));
        for (int i = 0; i < 3; i++)
            limiter.Allow("key1");
        limiter.Allow("key1").Should().BeFalse();
    }

    [Test]
    public void Allow_ReturnsTrue_AfterWindowExpiry()
    {
        var limiter = new RateLimiter(2, TimeSpan.FromMilliseconds(100));
        limiter.Allow("key1").Should().BeTrue();
        limiter.Allow("key1").Should().BeTrue();
        limiter.Allow("key1").Should().BeFalse();

        Thread.Sleep(150);

        limiter.Allow("key1").Should().BeTrue();
    }

    [Test]
    public void Allow_IndependentKeys_AreIsolated()
    {
        var limiter = new RateLimiter(1, TimeSpan.FromSeconds(1));
        limiter.Allow("keyA").Should().BeTrue();
        limiter.Allow("keyA").Should().BeFalse();
        limiter.Allow("keyB").Should().BeTrue();
    }

    [Test]
    public void Sweep_DoesNotThrow()
    {
        var limiter = new RateLimiter(1, TimeSpan.FromMilliseconds(100));
        limiter.Allow("expired").Should().BeTrue();
        limiter.Allow("current").Should().BeTrue();

        Thread.Sleep(150);

        var act = () => limiter.Sweep();
        act.Should().NotThrow();
    }

    [Test]
    public void Sweep_PreservesCurrent_WhenNoExpiration()
    {
        var limiter = new RateLimiter(2, TimeSpan.FromMilliseconds(500));
        limiter.Allow("a").Should().BeTrue();
        limiter.Allow("b").Should().BeTrue();
        limiter.Allow("c").Should().BeTrue();

        limiter.Sweep();

        // "a" has count=1 so far. With limit=2, it can still accept one more
        limiter.Allow("a").Should().BeTrue();
    }
}
