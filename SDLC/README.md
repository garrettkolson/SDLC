# SDLC Agent

Self-directed AI agent for full Software Development Lifecycle — from requirements to retrospective.

## Architecture

```
SDLC.Contracts    ← Shared records, enums, stage definitions
SDLC.Infrastructure  ← ArtifactStore (SQLite + file), StageGateStore
SDLC.Orchestrator  ← PipelineRunnerService, StageGateStep, process factory
SDLC.Agents        ← ResearchStep, BuildStep, IKernel, prompt builders
SDLC.Notifications ← SlackNotificationService (webhook)
SDLC.Dashboard     ← Blazor Server, SdlcRunService
SDLC.Telemetry     ← IPipelineTelemetry, step/gate/pipeline events
SDLC.Integration.Tests ← Real SQLite pipelines
```

## Pipeline Stages

```
Research → Requirements → Design → Gate → Build → Learn
```

Each stage produces an artifact stored in SQLite + file system. Stage gates require human approval before proceeding (except Research→Requirements which auto-advances).

## Project Layout

| Project | Description |
|---------|-------------|
| `src/SDLC.Contracts` | Core data types: `SdlcArtifact`, `ResearchBrief`, `RequirementsSpec`, `ArchitectureRecord`, `BuildResult`, `LearnReport`, `SdlcStage`, `ArtifactStatus`, `GateStatus`, `GateDecision`, `ModelRoutingConfig` |
| `src/SDLC.Infrastructure` | `ArtifactStore` (SQLite Dapper + file), `StageGateStore`, `IStageGateStore`, `IArtifactStore`, `StageGate` |
| `src/SDLC.Orchestrator` | `PipelineRunnerService` (enqueue, resume gates), `StageGateStep` (approval flow), `ISdlcProcessFactory`, `IKernelProcess`, `IProcessRuntime` |
| `src/SDLC.Agents` | `IKernel`, `IKernelFactory`, `AgentKernelFactory`, `ResearchStep` (ReAct loop, 3-attempt), `BuildStep` (SWE-AF submit/poll), `ResearchPrompts`, `RequirementsPrompts`, `DesignPrompts`, `LearnPrompts` |
| `src/SDLC.Notifications` | `INotificationService`, `SlackNotificationService` (POSTs JSON to webhook) |
| `src/SDLC.Dashboard` | Blazor Server app, `SdlcRunService` (run summaries, gate approve/reject) |
| `src/SDLC.Telemetry` | `IPipelineTelemetry` (step complete/failed, gate approve/reject, pipeline run lifecycle) |

## Building

```bash
# Build all projects
dotnet build

# Run all tests
dotnet test

# Run a specific phase
dotnet test tests/SDLC.Contracts.Tests
dotnet test tests/SDLC.Infrastructure.Tests
dotnet test tests/SDLC.Orchestrator.Tests
dotnet test tests/SDLC.Agents.Tests
dotnet test tests/SDLC.Notifications.Tests
dotnet test tests/SDLC.Dashboard.Tests
dotnet test tests/SDLC.Integration.Tests
dotnet test tests/SDLC.Telemetry.Tests
```

## Running

```bash
# Start the dashboard
dotnet run --project src/SDLC.Dashboard/SDLC.Dashboard.csproj
```

## Tests

136 tests across 8 test projects.

| Test Project | Tests | Focus |
|-------------|-------|-------|
| `SDLC.Contracts.Tests` | 40 | Record properties, defaults, enum values |
| `SDLC.Infrastructure.Tests` | 13 | Artifact CRUD, gate lifecycle, file storage |
| `SDLC.Orchestrator.Tests` | 23 | Stage gate steps, pipeline runner, event emission |
| `SDLC.Agents.Tests` | 20 | ReAct loop, SWE-AF integration, prompt builders |
| `SDLC.Notifications.Tests` | 6 | Slack webhook JSON payload, content type |
| `SDLC.Dashboard.Tests` | 10 | Run service, gate approve/reject, artifact summaries |
| `SDLC.Integration.Tests` | 12 | Full SQLite pipelines, artifact→gate flows |
| `SDLC.Telemetry.Tests` | 8 | Step/gate/pipeline event recording and retrieval |

## Key Interfaces

```csharp
// Pipeline orchestration
interface ISdlcProcessFactory { Task StartAsync(SdlcRunConfig config); }
class PipelineRunnerService { Task EnqueueAsync(SdlcRunConfig); Task ResumeGateAsync(runId, gateId, decision); }

// Agent execution
interface IKernel { Task<string> CompleteAsync(string systemPrompt, string userMessage, CancellationToken ct); }
interface IKernelFactory { IKernel CreateForStage(SdlcStage stage); }

// Artifact persistence
interface IArtifactStore { Task SaveAsync(SdlcArtifact); Task<List<SdlcArtifact>> GetAllForRunAsync(Guid runId); }

// Gate management
interface IStageGateStore { Task<StageGate> CreateGateAsync(SdlcArtifact); Task ResolveAsync(Guid, GateDecision, string?); }

// Notifications
interface INotificationService { Task SendApprovalRequestAsync(StageGate gate); }

// Telemetry
interface IPipelineTelemetry { Task RecordStepCompletedAsync(SdlcStage, string); Task RecordGateApprovedAsync(Guid); }

// Dashboard
interface ISdlcRunService { Task<IReadOnlyList<RunSummary>> GetActiveRunsAsync(); Task ApproveGateAsync(Guid); }
```

## Configuration

**Model routing** (`src/SDLC.Contracts/ModelRoutingConfig.cs`): Maps each stage to a model endpoint.

```csharp
var config = new ModelRoutingConfig {
    StageEndpoints = {
        [SdlcStage.Research]   = ModelEndpoint.Local27B,
        [SdlcStage.Requirements] = ModelEndpoint.Local27B,
        [SdlcStage.Design]     = ModelEndpoint.Local27B,
        [SdlcStage.Build]      = ModelEndpoint.Local27B,
        [SdlcStage.Learn]      = ModelEndpoint.LocalMoE,
    }
};
```

**Slack webhook** (`src/SDLC.Notifications/`): Configure via `HttpClient` with base URL pointing to Slack incoming webhook endpoint.

**SQLite** (`src/SDLC.Infrastructure/`): Connection string and file storage base path configurable on `ArtifactStore` and `StageGateStore` constructors.

## Test Strategy

- **Unit tests**: NSubstitute mocks + capturing contexts for async operations
- **Integration tests**: Real SQLite (temp file) + real file system, single-threaded execution
- **No WebMock**: Custom `HttpMessageHandler` for HTTP tests (no external dependency)
- **Reusable test doubles**: `FakeHttpHandler`, `TestArtifactStore`, `TestGateStore`, `TestRunner`, `TestSweAfClient`
