namespace SDLC.Contracts;

public static class SdlcEvents
{
    public const string RunStarted          = "sdlc.run.started";
    public const string RunComplete         = "sdlc.run.complete";
    public const string GatePending         = "sdlc.gate.pending";
    public const string GateApproved        = "sdlc.gate.approved";
    public const string GateRejected        = "sdlc.gate.rejected";
    public const string ResearchComplete    = "sdlc.stage.research.complete";
    public const string RequirementsComplete = "sdlc.stage.requirements.complete";
    public const string DesignComplete      = "sdlc.stage.design.complete";
    public const string BuildComplete       = "sdlc.stage.build.complete";
    public const string LearnComplete       = "sdlc.stage.learn.complete";
}
