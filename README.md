# SDLC Agent

Self-directed AI agent for the full Software Development Lifecycle — from requirements gathering to retrospective.

## Overview

SDLC Agent orchestrates a 5-stage pipeline (Research → Requirements → Design → Build → Learn) driven by AI models with human-in-the-loop stage gates. Each stage produces a typed artifact persisted to SQLite + file system. Gates require human approval before advancing to the next stage.

```
Research → Requirements → [Gate] → Design → [Gate] → Build → Learn
```

## Quick Start

```bash
cd SDLC
dotnet build
dotnet test
dotnet run --project src/SDLC.Dashboard/SDLC.Dashboard.csproj
```

## Architecture

```
SDLC.Contracts          ← Shared records, enums, stage definitions
SDLC.Infrastructure     ← ArtifactStore (SQLite + file), StageGateStore
SDLC.Orchestrator       ← PipelineRunnerService, StageGateStep, process factory
SDLC.Agents             ← ResearchStep, BuildStep, IKernel, prompt builders
SDLC.Notifications      ← SlackNotificationService (webhook)
SDLC.Dashboard          ← Blazor Server, SdlcRunService
SDLC.Telemetry          ← IPipelineTelemetry, step/gate/pipeline events
SDLC.Integration.Tests  ← Real SQLite pipelines
```

### Project Reference Graph

```
Contracts  → (no deps - leaf)
Infrastructure → Contracts
Orchestrator → Contracts, Infrastructure
Agents     → Contracts, Infrastructure
Notifications → Contracts, Infrastructure
Dashboard  → Contracts, Infrastructure, Orchestrator
Telemetry  → Contracts
```

## Pipeline Stages

| Stage | Artifact | Description | Gate? |
|-------|----------|-------------|-------|
| Research | `ResearchBrief` | ReAct loop with self-critique (3 attempts) | No (auto-advance) |
| Requirements | `RequirementsSpec` | Produces acceptance criteria | Yes |
| Design | `ArchitectureRecord` | Architecture decision record + Mermaid diagram | Yes |
| Build | `BuildResult` | Triggers SWE-AF, polls for completion | No |
| Learn | `LearnReport` | Retrospective with feedback items | No |

## Building & Testing

```bash
# Build all 15 projects
dotnet build

# Run all 152 tests
dotnet test

# Run a single test project
dotnet test tests/SDLC.Agents.Tests

# Integration tests (require Docker)
dotnet test --filter Category=Integration
```

### Test Coverage

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

### Model Routing

Each stage maps to a model endpoint via `ModelRoutingConfig` (configurable per run from the Dashboard).

```csharp
var config = new ModelRoutingConfig {
    StageEndpoints = {
        [SdlcStage.Research]    = ModelEndpoint.Local27B,
        [SdlcStage.Requirements] = ModelEndpoint.Local27B,
        [SdlcStage.Design]      = ModelEndpoint.Local27B,
        [SdlcStage.Build]       = ModelEndpoint.Local27B,
        [SdlcStage.Learn]       = ModelEndpoint.LocalMoE,
    }
};
```

### SQLite

Connection string and file storage base path configurable on `ArtifactStore` and `StageGateStore` constructors.

### Slack Webhook

Configure via `HttpClient` in `SDLC.Notifications` with base URL pointing to Slack incoming webhook.

## Test Strategy

- **Unit tests**: NSubstitute mocks + capturing contexts for async operations
- **Integration tests**: Real SQLite (temp file) + real file system, single-threaded
- **No WebMock**: Custom `HttpMessageHandler` for HTTP tests
- **Reusable test doubles**: `FakeHttpHandler`, `TestArtifactStore`, `TestGateStore`, `TestRunner`, `TestSweAfClient`

## Project Docs

| Doc | Purpose |
|-----|---------|
| [SDLC/README.md](SDLC/README.md) | Code reference, interfaces, build commands |
| [SDLC/AGENTS.md](SDLC/AGENTS.md) | Workflow conventions, naming, pitfalls |
| [SDLC-PRODUCTION-ROADMAP.md](SDLC-PRODUCTION-ROADMAP.md) | Phase-by-phase production readiness plan |
| [SDLC-PRODUCTION-BLOCKERS.md](SDLC-PRODUCTION-BLOCKERS.md) | Critical blockers with severity and mitigations |
| [sdlc-agent-implementation-plan-1.md](sdlc-agent-implementation-plan-1.md) | Original architecture and implementation plan |
| [sdlc-agent-test-plan.md](sdlc-agent-test-plan.md) | TDD test specifications per project |

## Status

- **Tests**: 152 pass across 8 test projects
- **Production readiness**: ~80% (Phases 0, 1, 8 complete)
- **Critical gaps**: Build/Learn stages not wired, gate rejection deadlock, missing dashboard auth

See [SDLC-PRODUCTION-ROADMAP.md](SDLC-PRODUCTION-ROADMAP.md) for the full phase breakdown and checklist.
