using FluentAssertions;
using NUnit.Framework;
using SDLC.Contracts;

namespace SDLC.Contracts.Tests;

[TestFixture]
public class ModelRoutingConfigTests
{
    [Test]
    public void Default_HasAllStagesConfigured()
    {
        var config = ModelRoutingConfig.Default;
        config.StageEndpoints.Should().ContainKey(SdlcStage.Research);
        config.StageEndpoints.Should().ContainKey(SdlcStage.Requirements);
        config.StageEndpoints.Should().ContainKey(SdlcStage.Design);
        config.StageEndpoints.Should().ContainKey(SdlcStage.Build);
        config.StageEndpoints.Should().ContainKey(SdlcStage.Learn);
    }

    [Test]
    public void GetEndpoint_ReturnsConfiguredEndpoint()
    {
        var config = ModelRoutingConfig.Default;
        config.GetEndpoint(SdlcStage.Research).Should().Be(ModelEndpoint.Local27B);
        config.GetEndpoint(SdlcStage.Requirements).Should().Be(ModelEndpoint.Local27B);
        config.GetEndpoint(SdlcStage.Design).Should().Be(ModelEndpoint.Local27B);
        config.GetEndpoint(SdlcStage.Build).Should().Be(ModelEndpoint.Local27B);
        config.GetEndpoint(SdlcStage.Learn).Should().Be(ModelEndpoint.LocalMoE);
    }

    [Test]
    public void GetEndpoint_UnknownStage_ReturnsDefaultLocal27B()
    {
        var config = new ModelRoutingConfig();
        config.GetEndpoint((SdlcStage)99).Should().Be(ModelEndpoint.Local27B);
    }

    [Test]
    public void GetEndpoint_CustomRouting_RespectsCustomConfig()
    {
        var config = new ModelRoutingConfig
        {
            StageEndpoints = new Dictionary<SdlcStage, ModelEndpoint>
            {
                [SdlcStage.Research] = ModelEndpoint.LocalMoE,
            }
        };
        config.GetEndpoint(SdlcStage.Research).Should().Be(ModelEndpoint.LocalMoE);
        config.GetEndpoint(SdlcStage.Build).Should().Be(ModelEndpoint.Local27B); // fallback
    }
}
