# Automated SDLC Agent Implementation Plan
**SmallWerks / SWE-AF Integration | .NET + Semantic Kernel**

---

## 1. System Architecture Overview

```
┌─────────────────────────────────────────────────────────────────────┐
│                        SDLC Orchestrator                            │
│              (ASP.NET Core + Semantic Kernel Process)               │
│                                                                     │
│  ┌──────────┐  ┌──────────┐  ┌──────────┐  ┌────────┐  ┌──────┐  │
│  │ Research │→ │  Ideation│→ │  Design  │→ │ Build  │→ │Learn │  │
│  │  Agent   │  │ & Reqs   │  │ & Arch   │  │ Agent  │  │Agent │  │
│  │ (Stage 1)│  │ (Stage 2)│  │ (Stage 3)│  │(Stg 4) │  │(Stg5)│  │
│  └──────────┘  └──────────┘  └──────────┘  └────────┘  └──────┘  │
│        ↓              ↓             ↓            ↓           ↓     │
│  ┌─────────────────────────────────────────────────────────────┐   │
│  │              Stage Gate Manager (HITL)                      │   │
│  │   Suspends pipeline → Notifies Slack/Teams → Awaits signal  │   │
│  └─────────────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────────┘
         │                    │                       │
         ▼                    ▼                       ▼
  ┌─────────────┐    ┌────────────────┐    ┌──────────────────┐
  │  Artifact   │    │  Blazor        │    │  SWE-AF HTTP     │
  │  Store      │    │  Dashboard     │    │  API             │
  │  (SQLite +  │    │  (Stage editor │    │  (Build trigger  │
  │   FS vol)   │    │   Model config │    │   + polling)     │
  └─────────────┘    │   Run monitor) │    └──────────────────┘
                     └────────────────┘
                             │
                    ┌────────┴────────┐
                    │  Slack / Teams  │
                    │  Webhooks       │
                    └─────────────────┘
                             │
                    ┌────────┴────────┐
                    │ OTel .NET SDK   │
                    │     ↓           │
                    │ Aspire Dashboard│
                    │  (Docker)       │
                    └─────────────────┘
```

---

## 2. Solution Structure

```
SDLC/
├── src/
│   ├── SDLC.Orchestrator/        # SK Process engine, pipeline runner
│   ├── SDLC.Dashboard/           # Blazor Server UI
│   ├── SDLC.Agents/              # All stage agent implementations
│   │   ├── Research/
│   │   ├── Requirements/
│   │   ├── Design/
│   │   ├── Build/
│   │   └── Learn/
│   ├── SDLC.Contracts/           # Shared types, artifact models, events
│   ├── SDLC.Infrastructure/      # SK kernel factory, HTTP clients, OTel setup
│   └── SDLC.Notifications/       # Slack / Teams webhook integration
├── docker/
│   ├── docker-compose.yml
│   └── aspire-dashboard/
├── tests/
│   ├── SDLC.Agents.Tests/
│   └── SDLC.Orchestrator.Tests/
└── SDLC.sln
```

### Key NuGet Dependencies

| Package | Purpose |
|---|---|
| `Microsoft.SemanticKernel` | Core SK |
| `Microsoft.SemanticKernel.Process.Runtime.InProcess` | SK Process Framework |
| `Microsoft.SemanticKernel.Agents.Core` | Agent abstractions |
| `OpenTelemetry.Extensions.Hosting` | OTel SDK host integration |
| `OpenTelemetry.Exporter.OpenTelemetryProtocol` | OTLP exporter to Aspire |
| `OpenTelemetry.Instrumentation.Http` | Auto-instrument HttpClient |
| `Serilog.Extensions.Hosting` + `Serilog.Sinks.OpenTelemetry` | Structured logging → OTel |
| `SlackNet` or `Microsoft.Bot.Builder` | Slack / Teams integration |
| `Microsoft.Data.Sqlite` + `Dapper` | Artifact store |

---

## 3. Core Domain: Artifact Model

Every stage produces a typed artifact that flows through the pipeline. All artifacts share a base contract and are persisted to the store.

```csharp
// SDLC.Contracts

public abstract record SdlcArtifact
{
    public Guid RunId          { get; init; }
    public Guid ArtifactId     { get; init; } = Guid.NewGuid();
    public SdlcStage Stage     { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public ArtifactStatus Status { get; init; } = ArtifactStatus.Draft;
    public string? HumanNotes  { get; init; }   // editable in dashboard
}

public record ResearchBrief       : SdlcArtifact { public string Content { get; init; } = ""; }
public record RequirementsSpec    : SdlcArtifact { public string Content { get; init; } = ""; 
                                                   public List<AcceptanceCriterion> Criteria { get; init; } = []; }
public record ArchitectureRecord  : SdlcArtifact { public string Content { get; init; } = ""; 
                                                   public string DiagramMermaid { get; init; } = ""; }
public record BuildResult         : SdlcArtifact { public bool Success { get; init; }
                                                   public string SweAfRunId { get; init; } = "";
                                                   public string Logs { get; init; } = ""; }
public record LearnReport         : SdlcArtifact { public string Retrospective { get; init; } = "";
                                                   public List<string> FeedbackItems { get; init; } = []; }

public enum SdlcStage   { Research, Requirements, Design, Build, Learn }
public enum ArtifactStatus { Draft, PendingReview, Approved, Rejected }
```

---

## 4. Pipeline State Machine (SK Process Framework)

The SK Process Framework handles the durable state machine. Each stage is a `KernelProcessStep`. Stage gates are external event suspension points.

```csharp
// SDLC.Orchestrator/SdlcProcess.cs

public static class SdlcProcess
{
    public static KernelProcess Build()
    {
        var builder = new ProcessBuilder("SdlcPipeline");

        // Register steps
        var research      = builder.AddStepFromType<ResearchStep>();
        var requirements  = builder.AddStepFromType<RequirementsStep>();
        var design        = builder.AddStepFromType<DesignStep>();
        var build         = builder.AddStepFromType<BuildStep>();
        var learn         = builder.AddStepFromType<LearnStep>();
        var gateManager   = builder.AddStepFromType<StageGateStep>();

        // Wire: Start → Research
        builder.OnInputEvent(SdlcEvents.RunStarted)
               .SendEventTo(new ProcessFunctionTargetBuilder(research));

        // Research complete → Gate → Requirements
        research.OnEvent(SdlcEvents.ResearchComplete)
                .SendEventTo(new ProcessFunctionTargetBuilder(gateManager));
        gateManager.OnEvent(SdlcEvents.GateApproved)
                   .SendEventTo(new ProcessFunctionTargetBuilder(requirements));
        gateManager.OnEvent(SdlcEvents.GateRejected)
                   .SendEventTo(new ProcessFunctionTargetBuilder(research));  // re-run

        // Requirements complete → Gate (with human edit window) → Design
        requirements.OnEvent(SdlcEvents.RequirementsComplete)
                    .SendEventTo(new ProcessFunctionTargetBuilder(gateManager));
        // ... (same pattern for Design → Build → Learn)

        return builder.Build();
    }
}
```

### Stage Gate Step

The gate step is the HITL integration point. It suspends process execution by emitting an external notification and awaiting an inbound event from the dashboard or Slack/Teams action.

```csharp
public class StageGateStep : KernelProcessStep
{
    [KernelFunction]
    public async Task RequestApprovalAsync(
        KernelProcessStepContext context,
        SdlcArtifact artifact,
        [FromKernelServices] INotificationService notifications,
        [FromKernelServices] IStageGateStore gateStore)
    {
        var gate = await gateStore.CreateGateAsync(artifact);

        // Non-blocking: notify and record. Process suspends here.
        await notifications.SendApprovalRequestAsync(gate);

        // Emit suspension event — the Process framework awaits external resume
        await context.EmitEventAsync(new() 
        { 
            Id = SdlcEvents.GatePending, 
            Data = gate.GateId 
        });
    }
}
```

External resumption (from dashboard button or Slack action handler):

```csharp
// Called by Dashboard controller or Slack webhook handler
public async Task ResumeGateAsync(Guid runId, Guid gateId, GateDecision decision, string? notes)
{
    var eventId = decision == GateDecision.Approved 
        ? SdlcEvents.GateApproved 
        : SdlcEvents.GateRejected;

    await _processRuntime.SendEventAsync(runId, new KernelProcessEvent 
    { 
        Id = eventId, 
        Data = new GateResolution(gateId, decision, notes) 
    });
}
```

---

## 5. Stage Agent Implementations

Each stage is a `ChatCompletionAgent` hosted inside its SK Process step. The kernel for each agent is resolved from a factory that applies per-stage model routing config.

### Agent Kernel Factory

```csharp
// SDLC.Infrastructure/AgentKernelFactory.cs

public class AgentKernelFactory
{
    private readonly ModelRoutingConfig _routing;

    public Kernel CreateForStage(SdlcStage stage)
    {
        var endpoint = _routing.GetEndpoint(stage);   // configurable per run

        var builder = Kernel.CreateBuilder()
            .AddOpenAIChatCompletion(
                modelId:  endpoint.ModelId,
                endpoint: new Uri(endpoint.BaseUrl),
                apiKey:   endpoint.ApiKey ?? "none");

        builder.Services.AddOpenTelemetry(); // OTel auto-traces SK calls
        return builder.Build();
    }
}
```

### Model Routing Config

```csharp
public class ModelRoutingConfig
{
    // Stored in DB per-run; editable in Dashboard before run starts
    public Dictionary<SdlcStage, ModelEndpoint> StageEndpoints { get; set; } = new()
    {
        [SdlcStage.Research]      = ModelEndpoint.Local27B,
        [SdlcStage.Requirements]  = ModelEndpoint.Local27B,
        [SdlcStage.Design]        = ModelEndpoint.Local27B,
        [SdlcStage.Build]         = ModelEndpoint.Local27B,   // SWE-AF uses its own routing
        [SdlcStage.Learn]         = ModelEndpoint.LocalMoE,   // lighter model fine for retrospectives
    };
}

public record ModelEndpoint(string ModelId, string BaseUrl, string? ApiKey = null)
{
    public static readonly ModelEndpoint Local27B  = new("codgician/Qwen3.5-27B-...", "http://localhost:8000/v1");
    public static readonly ModelEndpoint LocalMoE  = new("Qwen3.5-35B-A3B",           "http://localhost:8001/v1");
}
```

### Stage 1 — Research Agent

```csharp
public class ResearchStep : KernelProcessStep
{
    [KernelFunction]
    public async Task RunAsync(
        KernelProcessStepContext context,
        SdlcRunConfig config,
        [FromKernelServices] AgentKernelFactory kernelFactory,
        [FromKernelServices] IArtifactStore artifacts)
    {
        var kernel = kernelFactory.CreateForStage(SdlcStage.Research);
        var agent = new ChatCompletionAgent
        {
            Kernel = kernel,
            Name = "ResearchAgent",
            Instructions = ResearchPrompts.SystemPrompt
        };

        // ReAct loop with self-reflection
        var history = new ChatHistory();
        history.AddUserMessage(ResearchPrompts.BuildPrompt(config));

        ResearchBrief? brief = null;
        for (int attempt = 0; attempt < 3; attempt++)
        {
            var response = await agent.InvokeAsync(history).LastAsync();
            history.AddAssistantMessage(response.Content!);

            // Self-critique pass
            history.AddUserMessage(ResearchPrompts.CritiquePrompt);
            var critique = await agent.InvokeAsync(history).LastAsync();

            if (ResearchPrompts.IsSatisfactory(critique.Content!))
            {
                brief = ResearchPrompts.ParseBrief(response.Content!, config.RunId);
                break;
            }
            history.AddAssistantMessage(critique.Content!);
        }

        await artifacts.SaveAsync(brief!);
        await context.EmitEventAsync(new() { Id = SdlcEvents.ResearchComplete, Data = brief });
    }
}
```

Stages 2 (Requirements), 3 (Design), and 5 (Learn) follow the same ReAct+self-reflection pattern with stage-appropriate prompts.

### Stage 4 — Build Agent (SWE-AF Trigger)

```csharp
public class BuildStep : KernelProcessStep
{
    [KernelFunction]
    public async Task RunAsync(
        KernelProcessStepContext context,
        ArchitectureRecord architecture,
        RequirementsSpec spec,
        [FromKernelServices] ISweAfClient sweAf,
        [FromKernelServices] IArtifactStore artifacts,
        [FromKernelServices] ILogger<BuildStep> logger)
    {
        using var activity = SdlcTelemetry.StartBuildActivity(spec.RunId);

        // Translate artifacts into SWE-AF task payload
        var task = SweAfPayloadBuilder.Build(spec, architecture);
        logger.LogInformation("Triggering SWE-AF run for {RunId}", spec.RunId);

        var sweAfRunId = await sweAf.SubmitAsync(task);

        // Poll for completion with exponential backoff
        BuildResult? result = null;
        await foreach (var status in sweAf.PollAsync(sweAfRunId))
        {
            activity?.SetTag("sweaf.status", status.State);
            logger.LogInformation("SWE-AF {RunId} status: {State}", sweAfRunId, status.State);

            if (status.IsTerminal)
            {
                result = new BuildResult
                {
                    RunId = spec.RunId,
                    Stage = SdlcStage.Build,
                    SweAfRunId = sweAfRunId,
                    Success = status.State == SweAfState.Succeeded,
                    Logs = status.Logs ?? ""
                };
                break;
            }
        }

        await artifacts.SaveAsync(result!);
        await context.EmitEventAsync(new() { Id = SdlcEvents.BuildComplete, Data = result });
    }
}
```

### SWE-AF HTTP Client

```csharp
public interface ISweAfClient
{
    Task<string> SubmitAsync(SweAfTask task, CancellationToken ct = default);
    IAsyncEnumerable<SweAfStatus> PollAsync(string runId, CancellationToken ct = default);
}

public class SweAfHttpClient : ISweAfClient
{
    private readonly HttpClient _http;

    public async Task<string> SubmitAsync(SweAfTask task, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync("/api/runs", task, ct);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<SweAfRunCreated>(ct);
        return result!.RunId;
    }

    public async IAsyncEnumerable<SweAfStatus> PollAsync(
        string runId, 
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var delay = TimeSpan.FromSeconds(5);
        while (!ct.IsCancellationRequested)
        {
            var status = await _http.GetFromJsonAsync<SweAfStatus>($"/api/runs/{runId}", ct);
            yield return status!;
            if (status!.IsTerminal) yield break;
            await Task.Delay(delay, ct);
            delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 1.5, 60)); // backoff cap
        }
    }
}
```

---

## 6. Parallel Run Management

Parallel SDLC runs are isolated by `RunId`. Each run gets its own SK Process instance managed by the `PipelineRunnerService`.

```csharp
public class PipelineRunnerService : BackgroundService
{
    private readonly ConcurrentDictionary<Guid, Task> _activeRuns = new();
    private readonly Channel<SdlcRunConfig> _queue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var config in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            var runTask = RunPipelineAsync(config, stoppingToken);
            _activeRuns[config.RunId] = runTask;
            _ = runTask.ContinueWith(_ => _activeRuns.TryRemove(config.RunId, out _));
        }
    }

    private async Task RunPipelineAsync(SdlcRunConfig config, CancellationToken ct)
    {
        using var activity = SdlcTelemetry.StartRunActivity(config.RunId);
        var process = SdlcProcess.Build();
        var runtime = new InProcessRuntime();
        await runtime.StartAsync(process, new KernelProcessEvent 
        { 
            Id = SdlcEvents.RunStarted, 
            Data = config 
        });
        await runtime.RunUntilProcessEventAsync(SdlcEvents.RunComplete, ct);
    }
}
```

---

## 7. Blazor Dashboard

### Pages & Components

```
Dashboard/
├── Pages/
│   ├── Runs/
│   │   ├── Index.razor          # Active + historical runs list
│   │   ├── NewRun.razor         # Start run: project brief, model routing config
│   │   └── RunDetail.razor      # Per-run stage timeline + live status
│   ├── StageGate/
│   │   └── Review.razor         # Artifact editor + Approve/Reject
│   └── Config/
│       └── ModelRouting.razor   # Global model endpoint defaults
├── Components/
│   ├── StageTimeline.razor      # Visual pipeline progress indicator
│   ├── ArtifactEditor.razor     # Monaco-style markdown editor for specs/ADRs
│   ├── RunCard.razor            # Summary card for run list
│   └── ModelRoutingPanel.razor  # Per-stage endpoint dropdowns
```

### Stage Gate Review Page

This is the key HITL page. Linked from Slack/Teams notification:

```razor
@page "/gate/{GateId:guid}"
@inject IStageGateStore GateStore
@inject IPipelineRunnerService Runner

<h2>Stage Gate Review — @_gate?.Stage</h2>

@if (_gate is not null)
{
    <div class="artifact-meta">
        <span>Run: @_gate.RunId</span>
        <span>Stage: @_gate.Stage</span>
        <span>Created: @_gate.CreatedAt.ToLocalTime()</span>
    </div>

    <!-- Human-editable artifact content -->
    <ArtifactEditor @bind-Content="_editableContent" Stage="@_gate.Stage" />

    <!-- Model routing override for next stage -->
    <ModelRoutingPanel RunId="@_gate.RunId" />

    <div class="gate-actions">
        <button class="btn-approve" @onclick="ApproveAsync">✓ Approve</button>
        <button class="btn-reject"  @onclick="RejectAsync">✗ Reject — Re-run Stage</button>
    </div>

    @if (!string.IsNullOrEmpty(_validationError))
    {
        <div class="error">@_validationError</div>
    }
}

@code {
    [Parameter] public Guid GateId { get; set; }
    private StageGate? _gate;
    private string _editableContent = "";
    private string? _validationError;

    protected override async Task OnInitializedAsync()
    {
        _gate = await GateStore.GetAsync(GateId);
        _editableContent = _gate?.Artifact.Content ?? "";
    }

    private async Task ApproveAsync()
    {
        await GateStore.SaveEditedContentAsync(GateId, _editableContent);
        await Runner.ResumeGateAsync(_gate!.RunId, GateId, GateDecision.Approved, null);
    }

    private async Task RejectAsync()
    {
        await Runner.ResumeGateAsync(_gate!.RunId, GateId, GateDecision.Rejected, null);
    }
}
```

---

## 8. Notification Service (Slack / Teams)

```csharp
public class SlackNotificationService : INotificationService
{
    private readonly SlackApiClient _slack;
    private readonly DashboardUrlBuilder _urls;

    public async Task SendApprovalRequestAsync(StageGate gate)
    {
        var reviewUrl = _urls.ForGate(gate.GateId);

        var message = new SlackMessage
        {
            Channel = _config.ApprovalChannel,
            Blocks =
            [
                new SectionBlock { Text = $"*SDLC Stage Gate* — {gate.Stage}" },
                new SectionBlock { Text = $"Run `{gate.RunId}` requires review before proceeding." },
                new ActionsBlock
                {
                    Elements =
                    [
                        new ButtonElement { Text = "Review & Approve", Url = reviewUrl, Style = "primary" },
                        new ButtonElement { Text = "Reject",           Url = reviewUrl, Style = "danger"  }
                    ]
                }
            ]
        };

        await _slack.Chat.PostMessage(message);
    }
}
```

Teams integration uses an equivalent Adaptive Card with `openUrl` actions pointing to the same dashboard URL.

---

## 9. Observability: OTel .NET + Aspire Dashboard

### Setup in each service host

```csharp
// Program.cs (Orchestrator and Dashboard both register this)
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddSource("SDLC.*")          // all SK + custom activities
        .AddHttpClientInstrumentation()
        .AddAspNetCoreInstrumentation()
        .AddOtlpExporter(o => o.Endpoint = new Uri("http://aspire-dashboard:18889")))
    .WithMetrics(metrics => metrics
        .AddMeter("SDLC.*")
        .AddRuntimeInstrumentation()
        .AddOtlpExporter(o => o.Endpoint = new Uri("http://aspire-dashboard:18889")))
    .WithLogging(logging => logging
        .AddOtlpExporter(o => o.Endpoint = new Uri("http://aspire-dashboard:18889")));
```

### Custom Telemetry

```csharp
public static class SdlcTelemetry
{
    private static readonly ActivitySource Source = new("SDLC.Pipeline");
    private static readonly Meter Meter = new("SDLC.Pipeline");

    public static readonly Counter<long> RunsStarted    = Meter.CreateCounter<long>("sdlc.runs.started");
    public static readonly Counter<long> RunsCompleted  = Meter.CreateCounter<long>("sdlc.runs.completed");
    public static readonly Counter<long> GatesApproved  = Meter.CreateCounter<long>("sdlc.gates.approved");
    public static readonly Counter<long> GatesRejected  = Meter.CreateCounter<long>("sdlc.gates.rejected");
    public static readonly Histogram<double> StageDuration = 
        Meter.CreateHistogram<double>("sdlc.stage.duration_ms");

    public static Activity? StartRunActivity(Guid runId) =>
        Source.StartActivity("SdlcPipeline.Run")
              ?.SetTag("run.id", runId);

    public static Activity? StartBuildActivity(Guid runId) =>
        Source.StartActivity("SdlcPipeline.Build")
              ?.SetTag("run.id", runId)
              ?.SetTag("sweaf.trigger", true);
}
```

Semantic Kernel emits its own OTel traces automatically when `AddOpenTelemetry()` is present in the DI container — SK token counts, prompt execution spans, and tool call spans will all appear in the Aspire Dashboard with no additional code.

---

## 10. Docker Compose

```yaml
# docker/docker-compose.yml

services:

  aspire-dashboard:
    image: mcr.microsoft.com/dotnet/aspire-dashboard:latest
    ports:
      - "18888:18888"   # Dashboard UI
      - "18889:18889"   # OTLP receiver (gRPC)
    environment:
      DASHBOARD__OTLP__AUTHMODE: Unsecured

  orchestrator:
    build:
      context: ../src/SDLC.Orchestrator
    environment:
      OTEL_EXPORTER_OTLP_ENDPOINT: http://aspire-dashboard:18889
      SWEAF__BaseUrl: http://host.docker.internal:5100   # SWE-AF HTTP API
      ConnectionStrings__ArtifactDb: Data Source=/data/artifacts.db
      Notifications__Slack__Token: ${SLACK_BOT_TOKEN}
      Notifications__Slack__Channel: ${SLACK_APPROVAL_CHANNEL}
    volumes:
      - artifact-data:/data
    depends_on:
      - aspire-dashboard

  dashboard:
    build:
      context: ../src/SDLC.Dashboard
    ports:
      - "5200:8080"
    environment:
      OTEL_EXPORTER_OTLP_ENDPOINT: http://aspire-dashboard:18889
      Orchestrator__BaseUrl: http://orchestrator:8080
      ConnectionStrings__ArtifactDb: Data Source=/data/artifacts.db
    volumes:
      - artifact-data:/data
    depends_on:
      - orchestrator

volumes:
  artifact-data:
```

---

## 11. Artifact Store

SQLite + file system for human-readable artifacts:

```csharp
public class ArtifactStore : IArtifactStore
{
    // SQLite: stores metadata, status, run linkage
    // File system volume: stores artifact content as markdown files
    // Advantage: humans can read/diff artifacts directly on disk

    public async Task SaveAsync(SdlcArtifact artifact)
    {
        var path = ArtifactPath(artifact);
        await File.WriteAllTextAsync(path, artifact.Content);

        await _db.ExecuteAsync("""
            INSERT OR REPLACE INTO artifacts 
            (artifact_id, run_id, stage, status, file_path, created_at)
            VALUES (@ArtifactId, @RunId, @Stage, @Status, @FilePath, @CreatedAt)
            """, new { artifact.ArtifactId, artifact.RunId, artifact.Stage, 
                       artifact.Status, FilePath = path, artifact.CreatedAt });
    }

    private string ArtifactPath(SdlcArtifact a) =>
        Path.Combine(_basePath, a.RunId.ToString(), $"{a.Stage}.md");
}
```

---

## 12. Implementation Phases

### Phase 1 — Foundation (Weeks 1–2)
- Solution structure, `Contracts` project, artifact models
- `AgentKernelFactory` with model routing config
- `ArtifactStore` (SQLite + FS)
- Docker Compose with Aspire Dashboard
- OTel registration in all services
- SWE-AF HTTP client with polling

### Phase 2 — Build Stage Only (Weeks 3–4)
- `BuildStep` SK process step
- Single-stage pipeline (no gates yet) that calls SWE-AF and stores result
- Minimal Blazor dashboard: run list + run detail with live status
- Validate OTel traces end-to-end through Aspire Dashboard

### Phase 3 — Full Pipeline + HITL (Weeks 5–7)
- All 5 stage agents with ReAct+self-reflection loops
- `StageGateStep` with suspension/resume
- Slack notification service
- Gate review page in Blazor (artifact editor + approve/reject)
- Parallel run support via `PipelineRunnerService`

### Phase 4 — Model Routing + Polish (Week 8)
- Per-run model routing config in Dashboard (`NewRun.razor`)
- Per-stage endpoint override at gate review time
- Custom metrics dashboard in Aspire (stage durations, gate approval rates)
- Teams notification as alternative to Slack

---

## Key Design Decisions Summary

| Decision | Choice | Rationale |
|---|---|---|
| Orchestration | Semantic Kernel Process Framework | Durable state machine, SK-native, C# |
| HITL surface | Slack/Teams + Blazor Dashboard | Notify async, edit/approve in browser |
| Agent pattern | ReAct + self-reflection per stage | Reduces garbage output reaching gates |
| Artifact storage | SQLite metadata + markdown on FS | Human-readable, diffable, no extra infra |
| SWE-AF interface | HTTP with async polling + backoff | Clean boundary, SWE-AF owns its own execution |
| Observability | OTel SDK + Aspire Dashboard Docker | Zero-cost, SK auto-instrumented, backend-agnostic |
| Parallelism | `ConcurrentDictionary` + `Channel<T>` | Simple, no external queue needed at this scale |
| Model routing | Per-stage, per-run, dashboard-configurable | Lets you assign MoE to cheap stages, 27B to Build |
