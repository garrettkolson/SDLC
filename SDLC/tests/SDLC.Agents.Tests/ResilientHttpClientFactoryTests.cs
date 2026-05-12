using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NUnit.Framework;
using SDLC.Contracts;

namespace SDLC.Agents.Tests;

[TestFixture]
public class ResilientHttpClientFactoryTests
{
    [Test]
    public void CreateForStage_ReturnsNonNullHttpClient_ForAllStages()
    {
        var config = CreateConfig();
        var factory = new ResilientHttpClientFactory(config);

        foreach (SdlcStage stage in Enum.GetValues<SdlcStage>())
        {
            var client = factory.CreateForStage(stage);
            client.Should().NotBeNull();
        }
    }

    [Test]
    public void CreateForStage_SetTimeoutFromEndpointConfig()
    {
        var config = new ModelRoutingConfig
        {
            StageEndpoints = new Dictionary<SdlcStage, ModelEndpoint>
            {
                [SdlcStage.Research] = new("custom-model", "http://localhost:9000/v1", Timeout: TimeSpan.FromSeconds(30)),
                [SdlcStage.Build] = ModelEndpoint.Local27B,
            }
        };
        var factory = new ResilientHttpClientFactory(config);

        var researchClient = factory.CreateForStage(SdlcStage.Research);
        researchClient.Timeout.Should().Be(TimeSpan.FromSeconds(30));

        var buildClient = factory.CreateForStage(SdlcStage.Build);
        buildClient.Timeout.Should().Be(TimeSpan.FromMinutes(3));
    }

    [Test]
    public void CreateForStage_UsesDefaultTimeout_WhenEndpointHasNoTimeout()
    {
        var config = new ModelRoutingConfig
        {
            StageEndpoints = new Dictionary<SdlcStage, ModelEndpoint>
            {
                [SdlcStage.Research] = new("custom-model", "http://localhost:9000/v1"),
            }
        };
        var factory = new ResilientHttpClientFactory(config);

        var client = factory.CreateForStage(SdlcStage.Research);
        client.Timeout.Should().Be(TimeSpan.FromMinutes(3));
    }

    [Test]
    public void CreateForStage_SetsBearerToken_WhenApiKeyProvided()
    {
        var apiKey = "sk-test-key-123";
        var config = new ModelRoutingConfig
        {
            StageEndpoints = new Dictionary<SdlcStage, ModelEndpoint>
            {
                [SdlcStage.Research] = new("custom-model", "http://localhost:9000/v1", apiKey),
            }
        };
        var factory = new ResilientHttpClientFactory(config);

        var client = factory.CreateForStage(SdlcStage.Research);
        client.DefaultRequestHeaders.Authorization.Should().NotBeNull();
        client.DefaultRequestHeaders.Authorization!.Scheme.Should().Be("Bearer");
        client.DefaultRequestHeaders.Authorization.Parameter.Should().Be(apiKey);
    }

    [Test]
    public void CreateForStage_DoesNotSetBearerToken_WhenApiKeyIsEmpty()
    {
        var config = new ModelRoutingConfig
        {
            StageEndpoints = new Dictionary<SdlcStage, ModelEndpoint>
            {
                [SdlcStage.Research] = new("custom-model", "http://localhost:9000/v1", ""),
            }
        };
        var factory = new ResilientHttpClientFactory(config);

        var client = factory.CreateForStage(SdlcStage.Research);
        client.DefaultRequestHeaders.Authorization.Should().BeNull();
    }

    [Test]
    public void CreateForStage_DoesNotSetBearerToken_WhenApiKeyIsNull()
    {
        var config = new ModelRoutingConfig
        {
            StageEndpoints = new Dictionary<SdlcStage, ModelEndpoint>
            {
                [SdlcStage.Research] = new("custom-model", "http://localhost:9000/v1"),
            }
        };
        var factory = new ResilientHttpClientFactory(config);

        var client = factory.CreateForStage(SdlcStage.Research);
        client.DefaultRequestHeaders.Authorization.Should().BeNull();
    }

    [Test]
    public void CreateForStage_SetsBaseAddressFromEndpoint()
    {
        var config = new ModelRoutingConfig
        {
            StageEndpoints = new Dictionary<SdlcStage, ModelEndpoint>
            {
                [SdlcStage.Research] = new("custom-model", "http://inference.local:8080/v1/chat", null, null, TimeSpan.FromMinutes(2)),
            }
        };
        var factory = new ResilientHttpClientFactory(config);

        var client = factory.CreateForStage(SdlcStage.Research);
        client.BaseAddress.Should().Be(new Uri("http://inference.local:8080/v1/chat"));
    }

    [Test]
    public void CreateForStage_ReturnsDistinctHttpClients()
    {
        var config = CreateConfig();
        var factory = new ResilientHttpClientFactory(config);

        var client1 = factory.CreateForStage(SdlcStage.Research);
        var client2 = factory.CreateForStage(SdlcStage.Research);

        client1.Should().NotBeSameAs(client2);
    }

    [Test]
    public void CreateForStage_SetsBaseAddress_WhenUsingDefaultConfig()
    {
        var config = ModelRoutingConfig.Default;
        var factory = new ResilientHttpClientFactory(config);

        var client = factory.CreateForStage(SdlcStage.Research);
        client.BaseAddress.Should().Be(new Uri(ModelEndpoint.Local27B.BaseUrl));
    }

    private static ModelRoutingConfig CreateConfig()
    {
        return new ModelRoutingConfig
        {
            StageEndpoints = new Dictionary<SdlcStage, ModelEndpoint>
            {
                [SdlcStage.Research] = ModelEndpoint.Local27B,
                [SdlcStage.Requirements] = ModelEndpoint.Local27B,
                [SdlcStage.Design] = ModelEndpoint.Local27B,
                [SdlcStage.Build] = ModelEndpoint.Local27B,
                [SdlcStage.Learn] = ModelEndpoint.LocalMoE,
            }
        };
    }
}
