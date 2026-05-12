using FluentAssertions;
using NUnit.Framework;
using SDLC.Notifications;

namespace SDLC.Notifications.Tests;

[TestFixture]
public class DashboardUrlBuilderTests
{
    [Test]
    public void ForGate_WithTrailingSlash_ProducesCorrectUrl()
    {
        var builder = new DashboardUrlBuilder("http://localhost:8080/");
        var gateId = Guid.Parse("00000000-0000-0000-0000-000000000001");

        var url = builder.ForGate(gateId);
        url.Should().Be("http://localhost:8080/gate/00000000-0000-0000-0000-000000000001");
    }

    [Test]
    public void ForGate_WithoutTrailingSlash_ProducesCorrectUrl()
    {
        var builder = new DashboardUrlBuilder("http://localhost:8080");
        var gateId = Guid.Parse("00000000-0000-0000-0000-000000000001");

        var url = builder.ForGate(gateId);
        url.Should().Be("http://localhost:8080/gate/00000000-0000-0000-0000-000000000001");
    }

    [Test]
    public void ForGate_ProducesValidUrl()
    {
        var builder = new DashboardUrlBuilder("https://dashboard.example.com/api");
        var gateId = Guid.Parse("00000000-0000-0000-0000-000000000001");

        var url = builder.ForGate(gateId);
        var uri = new Uri(url);
        uri.Should().NotBeNull();
        uri.Scheme.Should().Be("https");
        uri.AbsolutePath.Should().Be("/api/gate/00000000-0000-0000-0000-000000000001");
    }
}
