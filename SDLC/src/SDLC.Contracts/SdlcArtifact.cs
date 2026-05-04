namespace SDLC.Contracts;

public abstract record SdlcArtifact
{
    public Guid RunId          { get; init; }
    public Guid ArtifactId     { get; init; } = Guid.NewGuid();
    public SdlcStage Stage     { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public ArtifactStatus Status { get; init; } = ArtifactStatus.Draft;
    public string? HumanNotes  { get; init; }
    public string Content     { get; init; } = "";
}

public record ResearchBrief       : SdlcArtifact { }
public record AcceptanceCriterion { public string Id { get; init; } = ""; public string Description { get; init; } = ""; }
public record RequirementsSpec    : SdlcArtifact { public List<AcceptanceCriterion> Criteria { get; init; } = []; }
public record ArchitectureRecord  : SdlcArtifact { public string DiagramMermaid { get; init; } = ""; }
public record BuildResult         : SdlcArtifact { public bool Success { get; init; } public string SweAfRunId { get; init; } = ""; public string Logs { get; init; } = ""; }
public record LearnReport         : SdlcArtifact { public string Retrospective { get; init; } = ""; public List<string> FeedbackItems { get; init; } = []; }
