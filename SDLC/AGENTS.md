# AGENTS.md — SDLC Agent Project Guide

## Directory Structure

```
src/
  SDLC.Contracts/       ← Shared types: records, enums, config
  SDLC.Infrastructure/  ← Persistence: ArtifactStore, StageGateStore
  SDLC.Orchestrator/    ← Pipeline: runner, stage gates, process factory
  SDLC.Agents/          ← AI agents: ResearchStep, BuildStep, prompts
  SDLC.Notifications/   ← Notifications: Slack webhook
  SDLC.Dashboard/       ← Blazor Server UI + SdlcRunService
  SDLC.Telemetry/       ← Telemetry: step/gate/pipeline events
tests/
  SDLC.Contracts.Tests/       ← 40 tests
  SDLC.Infrastructure.Tests/  ← 13 tests
  SDLC.Orchestrator.Tests/    ← 23 tests
  SDLC.Agents.Tests/          ← 20 tests
  SDLC.Notifications.Tests/   ← 6 tests
  SDLC.Dashboard.Tests/       ← 10 tests
  SDLC.Integration.Tests/     ← 12 tests
  SDLC.Telemetry.Tests/       ← 8 tests
```

## Workflow

TDD. Red → Green → Refactor. Write tests first, verify build, run, pass.

### Test Command

```bash
dotnet test                                          # all projects
dotnet test tests/SDLC.Agents.Tests -v minimal       # one project
```

136 tests pass. All projects must pass before merging.

## Project Reference Rules

```
Contracts  → no dependencies (leaf)
Infrastructure → Contracts
Orchestrator → Contracts, Infrastructure
Agents     → Contracts, Infrastructure
Notifications → Contracts, Infrastructure
Dashboard  → Contracts, Infrastructure, Orchestrator
Telemetry  → Contracts
```

Tests reference their own project + any project under test.

## Naming Conventions

- **Classes**: PascalCase, descriptive nouns (`ArtifactStore`, `PipelineRunnerService`)
- **Interfaces**: I prefix (`IArtifactStore`, `IPipelineTelemetry`)
- **Methods**: PascalCase verbs (`SaveAsync`, `GetActiveRunsAsync`)
- **Async suffix**: All I/O and awaited methods end with `Async`
- **Test classes**: `XXXTests` (plural)
- **Test methods**: `MethodName_State_ExpectedResult` (underscore-separated)
- **Test files**: same name as class

## Test Conventions

- NUnit 4.x framework, FluentAssertions for assertions, NSubstitute for mocking
- `[TestFixture, SingleThreaded]` for tests that touch shared resources (SQLite, files)
- Use `[SetUp]` / `[TearDown]` (not `[OneTimeSetUp]`)
- `[NotNull]` field annotation for `[NotNull]`-annotated fields instead of null-forgiving everywhere
- FakeHttpHandler pattern for HTTP tests (no WebMock dependency)
- CapturingContext pattern for IAsyncEnumerable and event capture (NSubstitute cannot mock IAsyncEnumerable well)
- Test doubles go as private nested classes in the test file

## Data Types

```csharp
SdlcStage     // Research, Requirements, Design, Build, Learn
ArtifactStatus // Draft, PendingReview, Approved, Rejected
GateStatus    // Pending, Approved, Rejected
GateDecision  // Approved, Rejected
ModelEndpoint // Local27B, LocalMoE, CloudEndpoint
```

All records are immutable (`init` only, or `readonly init`).

## Artifact Types

```
ResearchBrief       → stage: Research
RequirementsSpec    → stage: Requirements (contains Criteria)
ArchitectureRecord  → stage: Design (contains DiagramMermaid)
BuildResult         → stage: Build (Success, SweAfRunId, Logs)
LearnReport         → stage: Learn (Retrospective, FeedbackItems)
```

All derive from `SdlcArtifact` with `RunId`, `ArtifactId`, `Stage`, `CreatedAt`, `Status`, `Content`.

## Build Step Pattern

```csharp
// IAsyncEnumerable polling — use test doubles, not NSubstitute
public interface ISweAfClient {
    Task<string> SubmitAsync(SweAfTask, CancellationToken);
    IAsyncEnumerable<SweAfStatus> PollAsync(string runId, CancellationToken);
}
```

When testing code that consumes `IAsyncEnumerable`, create a concrete test double class rather than trying to use `Substitute.For<T>()` — NSubstitute cannot properly return `IAsyncEnumerable` values.

## Kernel Process Pattern

```csharp
// Lightweight SK Process replacement
public interface IKernelProcessStepContext {
    Task EmitEventAsync(KernelProcessEvent, CancellationToken);
}
public class KernelProcessEvent {
    public string Id { get; init; }
    public object? Data { get; init; }
}
```

## Stage Gate Pattern

```csharp
var gate = await gateStore.CreateGateAsync(artifact);  // creates Pending gate
await notifications.SendApprovalRequestAsync(gate);     // notifies
// wait for human decision...
await gateStore.ResolveAsync(gateId, GateDecision.Approved, notes);
```

## Telemetry Pattern

```csharp
ipipelineTelemetry.RecordStepCompletedAsync(stage, stepName);
ipipelineTelemetry.RecordStepFailedAsync(stage, stepName, ex);
ipipelineTelemetry.RecordGateApprovedAsync(gateId);
ipipelineTelemetry.RecordGateRejectedAsync(gateId);
```

## Code Style

- C# 13 records for data types, classes for behaviors
- Init-only properties
- Top-level `using` directives (C# 12 global usings where applicable)
- Nullable reference types enabled
- No `var` for explicit types — use `var` for anonymous types only
- No magic strings — use constants or config
- File-scoped namespaces preferred

## Common Pitfalls

1. **Don't create new SDLC stages without updating all switch expressions** — SdlcStage is referenced in switch expressions across ArtifactStore, StageGateStore, BuildStep, LearnPrompts
2. **SQLite is file-based** — use temp files in `[SetUp]`, delete in `[TearDown]`
3. **NSubstitute cannot proxy concrete classes** — use interfaces (IKernelFactory pattern)
4. **`const` fields don't work with reflection** — use `static readonly` for fields accessed via reflection in tests
5. **SweAfStatus.IsTerminal defaults to false** — test doubles must set `IsTerminal = true` to terminate polling loops
6. **SdlcArtifact.RunId** is required — `GetAllForRunAsync` passes it explicitly, `GetAsync` queries it from DB
7. **PipelineRunnerService.IsRunActive/ResumeGateAsync are virtual** — override in test doubles
