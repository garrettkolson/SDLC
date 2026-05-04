using System.Collections.Frozen;

namespace SDLC.Contracts;

public class ModelRoutingConfig
{
    public Dictionary<SdlcStage, ModelEndpoint> StageEndpoints { get; set; } = new()
    {
        [SdlcStage.Research]      = ModelEndpoint.Local27B,
        [SdlcStage.Requirements]  = ModelEndpoint.Local27B,
        [SdlcStage.Design]        = ModelEndpoint.Local27B,
        [SdlcStage.Build]         = ModelEndpoint.Local27B,
        [SdlcStage.Learn]         = ModelEndpoint.LocalMoE,
    };

    public static ModelRoutingConfig Default = new();

    public ModelEndpoint GetEndpoint(SdlcStage stage) => StageEndpoints.GetValueOrDefault(stage) ?? ModelEndpoint.Local27B;
}
