using SDLC.Contracts;

namespace SDLC.Agents;

public interface IKernelFactory
{
    IKernel CreateForStage(SdlcStage stage);
}
