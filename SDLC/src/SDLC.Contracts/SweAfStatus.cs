namespace SDLC.Contracts;

public class SweAfStatus
{
    public SweAfState State { get; init; }
    public bool IsTerminal { get; init; }
    public string? Logs { get; init; }
}
