namespace SDLC.Dashboard.Hubs;

public record GateResolvedMessage(Guid GateId, bool Approved, string? Notes);
public record RunStateChangedMessage(Guid RunId, string Status);
