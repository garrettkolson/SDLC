using FluentAssertions;
using SDLC.Contracts;
using NUnit.Framework;

namespace SDLC.Contracts.Tests;

public class ModelEndpointTests
{
    [Test]
    public void ModelEndpoint_Local27B_HasNonEmptyBaseUrl()
    {
        ModelEndpoint.Local27B.BaseUrl.Should().NotBeNullOrWhiteSpace();
    }

    [Test]
    public void ModelEndpoint_Local27B_HasNonEmptyModelId()
    {
        ModelEndpoint.Local27B.ModelId.Should().NotBeNullOrWhiteSpace();
    }

    [Test]
    public void ModelEndpoint_LocalMoE_HasNonEmptyBaseUrl()
    {
        ModelEndpoint.LocalMoE.BaseUrl.Should().NotBeNullOrWhiteSpace();
    }

    [Test]
    public void ModelEndpoint_Local27B_And_LocalMoE_HaveDifferentBaseUrls()
    {
        ModelEndpoint.Local27B.BaseUrl.Should().NotBe(ModelEndpoint.LocalMoE.BaseUrl);
    }

    [Test]
    public void ModelEndpoint_BaseUrl_MustBeValidUri()
    {
        Uri.TryCreate(ModelEndpoint.Local27B.BaseUrl, UriKind.Absolute, out _).Should().BeTrue();
        Uri.TryCreate(ModelEndpoint.LocalMoE.BaseUrl, UriKind.Absolute, out _).Should().BeTrue();
    }
}
