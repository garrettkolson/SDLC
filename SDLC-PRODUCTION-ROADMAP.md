# SDLC Agent — Production Readiness Roadmap

Generated from audit of current implementation vs `sdlc-agent-implementation-plan-1.md`.

Each phase is self-contained. Complete phases in order — later phases depend on earlier ones. Each task includes: what to change, which file, and what done looks like.

---

## Phase 0 — Blockers (Do First, Nothing Works Without These)

### 0.1 — `StageGateStore` Must Implement `IStageGateStore`

**File:** `SDLC/src/SDLC.Infrastructure/StageGateStore.cs`

**Problem:** `StageGateStore` has all the right methods but is not declared to implement the interface. DI registration as `IStageGateStore` will throw at startup.

**Fix:** Change class declaration:
```csharp
// Before
public class StageGateStore

// After
public class StageGateStore : IStageGateStore
```

**Done when:** `new StageGateStore(...) is IStageGateStore` is true.

---

### 0.2 — Remove Leftover Template Stubs

**Files:**
- `SDLC/src/SDLC.Contracts/Class1.cs`
- `SDLC/src/SDLC.Orchestrator/Class1.cs`
- `SDLC/src/SDLC.Notifications/Class1.cs`
- `SDLC/src/SDLC.Contracts/SdlcRunConfigTests.cs` ← test file in source project

**Fix:** Delete all four files. `SdlcRunConfigTests` already exists in `tests/SDLC.Contracts.Tests/`.

**Done when:** Files deleted, solution builds clean.

---

### 0.3 — Verify Solution File Exists

**Expected path:** `SDLC/SDLC.sln`

**Fix:** If missing, generate it:
```bash
cd SDLC
dotnet new sln -n SDLC
dotnet sln add src/SDLC.Contracts/SDLC.Contracts.csproj
dotnet sln add src/SDLC.Infrastructure/SDLC.Infrastructure.csproj
dotnet sln add src/SDLC.Orchestrator/SDLC.Orchestrator.csproj
dotnet sln add src/SDLC.Agents/SDLC.Agents.csproj
dotnet sln add src/SDLC.Notifications/SDLC.Notifications.csproj
dotnet sln add src/SDLC.Dashboard/SDLC.Dashboard.csproj
dotnet sln add src/SDLC.Telemetry/SDLC.Telemetry.csproj
dotnet sln add tests/SDLC.Contracts.Tests/SDLC.Contracts.Tests.csproj
dotnet sln add tests/SDLC.Infrastructure.Tests/SDLC.Infrastructure.Tests.csproj
dotnet sln add tests/SDLC.Orchestrator.Tests/SDLC.Orchestrator.Tests.csproj
dotnet sln add tests/SDLC.Agents.Tests/SDLC.Agents.Tests.csproj
dotnet sln add tests/SDLC.Notifications.Tests/SDLC.Notifications.Tests.csproj
dotnet sln add tests/SDLC.Dashboard.Tests/SDLC.Dashboard.Tests.csproj
dotnet sln add tests/SDLC.Integration.Tests/SDLC.Integration.Tests.csproj
dotnet sln add tests/SDLC.Telemetry.Tests/SDLC.Telemetry.Tests.csproj
```

**Done when:** `dotnet build` from `SDLC/` builds all 15 projects.

---

## Phase 1 — Real AI Execution

### 1.1 — Replace `DefaultKernel` Stub With Real vLLM HTTP Call

**File:** `SDLC/src/SDLC.Agents/AgentKernelFactory.cs`

**Problem:** `DefaultKernel.CompleteAsync` returns `$"Response for {_endpoint.ModelId}"` — a hardcoded string. No HTTP call is ever made.

**Fix:** Replace `DefaultKernel` with a real OpenAI-compatible HTTP client targeting the vLLM endpoint. Add `System.Net.Http.Json` reference if not present.

```csharp
public class DefaultKernel : IKernel
{
    private readonly ModelEndpoint _endpoint;
    private readonly HttpClient _http;

    public DefaultKernel(ModelEndpoint endpoint)
    {
        _endpoint = endpoint;
        _http = new HttpClient { BaseAddress = new Uri(endpoint.BaseUrl) };
        if (!string.IsNullOrEmpty(endpoint.ApiKey))
            _http.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", endpoint.ApiKey);
    }

    public async Task<string> CompleteAsync(string systemPrompt, string userMessage, CancellationToken ct = default)
    {
        var request = new
        {
            model = _endpoint.ModelId,
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user",   content = userMessage  }
            },
            temperature = 0.7,
            max_tokens = 4096
        };

        var response = await _http.PostAsJsonAsync("/v1/chat/completions", request, ct);
        response.EnsureSuccessStatusCode();

        using var doc = await System.Text.Json.JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        return doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString() ?? "";
    }
}
```

**Production considerations:**
- `HttpClient` should be injected (via `IHttpClientFactory`) rather than constructed per-kernel instance. Wire in DI when building Program.cs (Phase 5).
- `max_tokens` should be configurable per stage. Add to `ModelEndpoint` or `ModelRoutingConfig` if needed.

**Done when:** `ResearchStep` running against a live vLLM instance returns real model output.

---

### 1.2 — Fix `ResearchStep` Fallback Bug

**File:** `SDLC/src/SDLC.Agents/ResearchStep.cs`

**Problem:** When all 3 attempts are unsatisfactory, `history[history.Count - 1]` is a `"Critique: ..."` entry, not an `"AI: ..."` entry. The saved artifact content becomes the critique text.

**Root cause:** The loop adds critique to history unconditionally after every failed attempt, including the last one.

**Fix:** Track the last AI response separately from history:

```csharp
public async Task RunAsync(
    IKernelProcessStepContext context,
    SdlcRunConfig config,
    IKernelFactory kernelFactory,
    IArtifactStore artifacts,
    CancellationToken ct = default)
{
    var kernel = kernelFactory.CreateForStage(SdlcStage.Research);
    var history = new List<string>();
    history.Add(ResearchPrompts.BuildPrompt(config));

    ResearchBrief? brief = null;
    string lastAiResponse = "";

    for (int attempt = 0; attempt < MaxAttempts; attempt++)
    {
        var response = await kernel.CompleteAsync(ResearchPrompts.SystemPrompt, string.Join("\n", history), ct);
        lastAiResponse = response;
        history.Add($"AI: {response}");

        var critique = await kernel.CompleteAsync(ResearchPrompts.CritiquePrompt, response, ct);

        if (ResearchPrompts.IsSatisfactory(critique))
        {
            brief = ResearchPrompts.ParseBrief(response, config.RunId);
            break;
        }
        history.Add($"Critique: {critique}");
    }

    brief ??= new ResearchBrief { Content = lastAiResponse, RunId = config.RunId, Stage = SdlcStage.Research };

    await artifacts.SaveAsync(brief);
    await context.EmitEventAsync(new KernelProcessEvent { Id = SdlcEvents.ResearchComplete, Data = brief }, ct);
}
```

**Done when:** Unit test added: "when all attempts unsatisfactory, saved content equals last AI response, not critique text."

---

### 1.3 — Add `RequirementsStep`

**Create file:** `SDLC/src/SDLC.Agents/RequirementsStep.cs`

Mirrors `ResearchStep` pattern. Takes `ResearchBrief` as input, produces `RequirementsSpec`. Prompts already exist in `StagesPrompts.cs` as `RequirementsPrompts`.

```csharp
using Microsoft.Extensions.Logging;
using SDLC.Contracts;
using SDLC.Infrastructure;

namespace SDLC.Agents;

public class RequirementsStep
{
    public static int MaxAttempts = 3;

    public async Task RunAsync(
        IKernelProcessStepContext context,
        SdlcRunConfig config,
        ResearchBrief research,
        IKernelFactory kernelFactory,
        IArtifactStore artifacts,
        CancellationToken ct = default)
    {
        var kernel = kernelFactory.CreateForStage(SdlcStage.Requirements);
        var history = new List<string>();
        history.Add(RequirementsPrompts.BuildPrompt(config.ProjectBrief, research.Content));

        RequirementsSpec? spec = null;
        string lastAiResponse = "";

        for (int attempt = 0; attempt < MaxAttempts; attempt++)
        {
            var response = await kernel.CompleteAsync(RequirementsPrompts.SystemPrompt, string.Join("\n", history), ct);
            lastAiResponse = response;
            history.Add($"AI: {response}");

            var critique = await kernel.CompleteAsync(RequirementsPrompts.CritiquePrompt, response, ct);

            if (RequirementsPrompts.IsSatisfactory(critique))
            {
                spec = new RequirementsSpec { Content = response, RunId = config.RunId, Stage = SdlcStage.Requirements };
                break;
            }
            history.Add($"Critique: {critique}");
        }

        spec ??= new RequirementsSpec { Content = lastAiResponse, RunId = config.RunId, Stage = SdlcStage.Requirements };

        await artifacts.SaveAsync(spec);
        await context.EmitEventAsync(new KernelProcessEvent { Id = SdlcEvents.RequirementsComplete, Data = spec }, ct);
    }
}
```

**Done when:** Unit tests added covering: saves spec, emits event, retries on unsatisfactory, fallback uses last AI response.

---

### 1.4 — Add `DesignStep`

**Create file:** `SDLC/src/SDLC.Agents/DesignStep.cs`

Mirrors `RequirementsStep`. Takes `ResearchBrief` + `RequirementsSpec`, produces `ArchitectureRecord`. Uses `DesignPrompts`.

```csharp
public class DesignStep
{
    public static int MaxAttempts = 3;

    public async Task RunAsync(
        IKernelProcessStepContext context,
        SdlcRunConfig config,
        ResearchBrief research,
        RequirementsSpec spec,
        IKernelFactory kernelFactory,
        IArtifactStore artifacts,
        CancellationToken ct = default)
    {
        var kernel = kernelFactory.CreateForStage(SdlcStage.Design);
        var history = new List<string>();
        history.Add(DesignPrompts.BuildPrompt(config.ProjectBrief, research.Content, spec.Content));

        ArchitectureRecord? record = null;
        string lastAiResponse = "";

        for (int attempt = 0; attempt < MaxAttempts; attempt++)
        {
            var response = await kernel.CompleteAsync(DesignPrompts.SystemPrompt, string.Join("\n", history), ct);
            lastAiResponse = response;
            history.Add($"AI: {response}");

            var critique = await kernel.CompleteAsync(DesignPrompts.CritiquePrompt, response, ct);

            if (DesignPrompts.IsSatisfactory(critique))
            {
                record = new ArchitectureRecord { Content = response, RunId = config.RunId, Stage = SdlcStage.Design };
                break;
            }
            history.Add($"Critique: {critique}");
        }

        record ??= new ArchitectureRecord { Content = lastAiResponse, RunId = config.RunId, Stage = SdlcStage.Design };

        await artifacts.SaveAsync(record);
        await context.EmitEventAsync(new KernelProcessEvent { Id = SdlcEvents.DesignComplete, Data = record }, ct);
    }
}
```

**Done when:** Unit tests added (same pattern as `RequirementsStep` tests).

---

### 1.5 — Add `LearnStep`

**Create file:** `SDLC/src/SDLC.Agents/LearnStep.cs`

Takes `RequirementsSpec` + `BuildResult`, produces `LearnReport`. Uses `LearnPrompts`.

```csharp
public class LearnStep
{
    public static int MaxAttempts = 3;

    public async Task RunAsync(
        IKernelProcessStepContext context,
        SdlcRunConfig config,
        RequirementsSpec spec,
        BuildResult buildResult,
        IKernelFactory kernelFactory,
        IArtifactStore artifacts,
        CancellationToken ct = default)
    {
        var kernel = kernelFactory.CreateForStage(SdlcStage.Learn);
        var history = new List<string>();
        history.Add(LearnPrompts.BuildPrompt(config.ProjectBrief, buildResult.Logs, spec.Content));

        LearnReport? report = null;
        string lastAiResponse = "";

        for (int attempt = 0; attempt < MaxAttempts; attempt++)
        {
            var response = await kernel.CompleteAsync(LearnPrompts.SystemPrompt, string.Join("\n", history), ct);
            lastAiResponse = response;
            history.Add($"AI: {response}");

            var critique = await kernel.CompleteAsync(LearnPrompts.CritiquePrompt, response, ct);

            if (LearnPrompts.IsSatisfactory(critique))
            {
                report = new LearnReport
                {
                    Content = response,
                    Retrospective = response,
                    RunId = config.RunId,
                    Stage = SdlcStage.Learn
                };
                break;
            }
            history.Add($"Critique: {critique}");
        }

        report ??= new LearnReport { Content = lastAiResponse, Retrospective = lastAiResponse, RunId = config.RunId, Stage = SdlcStage.Learn };

        await artifacts.SaveAsync(report);
        await context.EmitEventAsync(new KernelProcessEvent { Id = SdlcEvents.LearnComplete, Data = report }, ct);
    }
}
```

**Done when:** Unit tests added.

---

### 1.6 — Fix `BuildStep` Null Dereference

**File:** `SDLC/src/SDLC.Agents/BuildStep.cs`

**Problem:** If `PollAsync` yields no terminal status (cancelled, empty stream), `result` is null and `result!` throws.

**Fix:** Guard before `SaveAsync`:

```csharp
if (result is null)
{
    result = new BuildResult
    {
        RunId = spec.RunId,
        Stage = SdlcStage.Build,
        SweAfRunId = sweAfRunId,
        Success = false,
        Logs = "Build timed out or was cancelled before a terminal status was received."
    };
}

await artifacts.SaveAsync(result);
```

**Done when:** Unit test added: "when PollAsync returns no terminal status, saves BuildResult with Success=false."

---

## Phase 2 — Pipeline Wiring

### 2.1 — Implement `ISdlcProcessFactory`

**Create file:** `SDLC/src/SDLC.Orchestrator/SdlcProcessFactory.cs`

This is the glue that sequences the stages. Because the SK Process Framework is not yet integrated (see Phase 4 option), implement as a sequential async runner for now. This is still fully testable and correct — replace internals with SK Process later without changing the interface.

```csharp
using Microsoft.Extensions.Logging;
using SDLC.Agents;
using SDLC.Contracts;
using SDLC.Infrastructure;
using SDLC.Notifications;

namespace SDLC.Orchestrator;

public class SdlcProcessFactory : ISdlcProcessFactory
{
    private readonly IKernelFactory _kernelFactory;
    private readonly IArtifactStore _artifactStore;
    private readonly IStageGateStore _gateStore;
    private readonly INotificationService _notifications;
    private readonly ILogger<SdlcProcessFactory> _logger;

    public SdlcProcessFactory(
        IKernelFactory kernelFactory,
        IArtifactStore artifactStore,
        IStageGateStore gateStore,
        INotificationService notifications,
        ILogger<SdlcProcessFactory> logger)
    {
        _kernelFactory = kernelFactory;
        _artifactStore = artifactStore;
        _gateStore = gateStore;
        _notifications = notifications;
        _logger = logger;
    }

    public ProcessHandle StartAsync(SdlcRunConfig config)
    {
        var task = RunPipelineAsync(config, CancellationToken.None);
        return new ProcessHandle(task);
    }

    private async Task RunPipelineAsync(SdlcRunConfig config, CancellationToken ct)
    {
        _logger.LogInformation("Pipeline started for run {RunId}", config.RunId);

        // Stage 1: Research
        var researchContext = new CapturingContext();
        await new ResearchStep().RunAsync(researchContext, config, _kernelFactory, _artifactStore, ct);
        var research = (ResearchBrief)researchContext.LastEvent!.Data!;

        // Stage 2: Requirements
        var reqContext = new CapturingContext();
        await new RequirementsStep().RunAsync(reqContext, config, research, _kernelFactory, _artifactStore, ct);
        var spec = (RequirementsSpec)reqContext.LastEvent!.Data!;

        // Gate: Requirements → Design
        await RequestGateApprovalAsync(spec, ct);

        // Stage 3: Design (loads latest approved spec from store)
        var latestSpec = await _artifactStore.GetLatestForRunAsync<RequirementsSpec>(config.RunId) ?? spec;
        var designContext = new CapturingContext();
        await new DesignStep().RunAsync(designContext, config, research, latestSpec, _kernelFactory, _artifactStore, ct);
        var architecture = (ArchitectureRecord)designContext.LastEvent!.Data!;

        // Gate: Design → Build
        await RequestGateApprovalAsync(architecture, ct);

        // Stage 4: Build
        var latestArch = await _artifactStore.GetLatestForRunAsync<ArchitectureRecord>(config.RunId) ?? architecture;
        var buildContext = new CapturingContext();
        // BuildStep requires ISweAfClient — inject via constructor when wiring DI
        throw new NotImplementedException("Wire ISweAfClient into SdlcProcessFactory");

        // Stage 5: Learn
        // var learnContext = new CapturingContext();
        // await new LearnStep().RunAsync(learnContext, config, latestSpec, buildResult, _kernelFactory, _artifactStore, ct);

        _logger.LogInformation("Pipeline complete for run {RunId}", config.RunId);
    }

    private async Task RequestGateApprovalAsync(SdlcArtifact artifact, CancellationToken ct)
    {
        var gateStep = new StageGateStep();
        var ctx = new CapturingContext();
        await gateStep.RequestApprovalAsync(ctx, artifact, _notifications, _gateStore, ct);
    }

    private sealed class CapturingContext : IKernelProcessStepContext
    {
        public KernelProcessEvent? LastEvent { get; private set; }

        public Task EmitEventAsync(KernelProcessEvent @event, CancellationToken ct = default)
        {
            LastEvent = @event;
            return Task.CompletedTask;
        }
    }
}
```

**Note:** Gate suspension (waiting for human approval) requires a mechanism for the pipeline task to pause and resume. See section 2.3 for the `TaskCompletionSource`-based gate wait.

**Done when:** Factory is registered in DI; a run can be started; stages 1–3 execute sequentially in a real end-to-end test.

---

### 2.2 — Wire `PipelineRunnerService.EnqueueAsync` to the Factory

**File:** `SDLC/src/SDLC.Orchestrator/SdlcProcess.cs`

**Problem:** `EnqueueAsync` adds to `_activeRuns` but never calls the factory.

**Fix:**

```csharp
public virtual Task EnqueueAsync(SdlcRunConfig config, CancellationToken ct = default)
{
    if (!_activeRuns.TryAdd(config.RunId, new object()))
        throw new InvalidOperationException($"Run {config.RunId} is already active.");

    _logger.LogInformation("Starting SDLC run {RunId}", config.RunId);

    var handle = _processFactory.StartAsync(config);
    _ = handle.Task.ContinueWith(t =>
    {
        _activeRuns.TryRemove(config.RunId, out _);
        if (t.IsFaulted)
            _logger.LogError(t.Exception, "Run {RunId} failed", config.RunId);
        else
            _logger.LogInformation("Run {RunId} completed", config.RunId);
    }, TaskScheduler.Default);

    return Task.CompletedTask;
}
```

**Done when:** `EnqueueAsync` starts the pipeline on a background task; `ActiveRunCount` decrements when done.

---

### 2.3 — Implement Gate Suspension / Resume

**Problem:** The pipeline needs to pause after emitting `GatePending` and wait for a human to approve/reject before continuing. Currently `ResumeGateAsync` does nothing.

**Approach:** Use a `ConcurrentDictionary<Guid, TaskCompletionSource<GateResolution>>` to hold pending gate completions.

**File:** `SDLC/src/SDLC.Orchestrator/SdlcProcess.cs` — modify `PipelineRunnerService`; also update `SdlcProcessFactory`.

**Step A — Add gate wait dictionary to `PipelineRunnerService`:**

```csharp
private readonly ConcurrentDictionary<Guid, TaskCompletionSource<GateResolution>> _pendingGates = new();

public Task<GateResolution> WaitForGateAsync(Guid gateId, CancellationToken ct)
{
    var tcs = new TaskCompletionSource<GateResolution>(TaskCreationOptions.RunContinuationsAsynchronously);
    _pendingGates[gateId] = tcs;
    ct.Register(() => tcs.TrySetCanceled(ct));
    return tcs.Task;
}

public override async Task ResumeGateAsync(Guid runId, Guid gateId, GateDecision decision, string? notes, CancellationToken ct = default)
{
    if (!_activeRuns.ContainsKey(runId))
        throw new InvalidOperationException($"No active run for {runId}");

    if (_pendingGates.TryRemove(gateId, out var tcs))
        tcs.TrySetResult(new GateResolution(gateId, decision, notes));
}
```

**Step B — Add `GateResolution` to `OrchestratorContracts.cs`:**

```csharp
public record GateResolution(Guid GateId, GateDecision Decision, string? Notes);
```

**Step C — In `SdlcProcessFactory.RequestGateApprovalAsync`, await the resolution:**

```csharp
private async Task<GateResolution> RequestGateApprovalAsync(
    SdlcArtifact artifact,
    PipelineRunnerService runner,
    CancellationToken ct)
{
    var gate = await _gateStore.CreateGateAsync(artifact);
    await _notifications.SendApprovalRequestAsync(gate);

    // Suspend until human acts
    var resolution = await runner.WaitForGateAsync(gate.GateId, ct);

    // Persist the decision
    await _gateStore.ResolveAsync(gate.GateId, resolution.Decision, resolution.Notes);

    if (resolution.Decision == GateDecision.Rejected)
        throw new GateRejectedException(gate.GateId, resolution.Notes);

    return resolution;
}
```

**Step D — Add `GateRejectedException`:**

```csharp
public class GateRejectedException : Exception
{
    public Guid GateId { get; }
    public GateRejectedException(Guid gateId, string? notes)
        : base($"Gate {gateId} was rejected: {notes}") => GateId = gateId;
}
```

**Step E — Handle rejection in `RunPipelineAsync`:** Catch `GateRejectedException` and re-run the preceding stage with the reviewer's edited artifact content already saved in the store.

**Done when:** End-to-end integration test: start run → run reaches gate → `ResumeGateAsync(Approved)` → pipeline continues to next stage.

---

### 2.4 — Fix `GetActiveRunsAsync` Hardcoded `Guid.Empty`

**File:** `SDLC/src/SDLC.Dashboard/Services/SdlcRunService.cs`

**Problem:**
```csharp
var runIds = new[] { Guid.Empty };
```

**Fix:** Add a method to `IArtifactStore` (and `ArtifactStore`) to list distinct run IDs, then use it:

```csharp
// Add to IArtifactStore in SDLC/src/SDLC.Infrastructure/Interfaces.cs
Task<List<Guid>> GetAllRunIdsAsync();

// Implement in ArtifactStore.cs
public async Task<List<Guid>> GetAllRunIdsAsync()
{
    using var conn = new SqliteConnection(_connectionString);
    await conn.OpenAsync();
    var ids = await conn.QueryAsync<string>("SELECT DISTINCT run_id FROM artifacts ORDER BY created_at DESC");
    return ids.Select(Guid.Parse).ToList();
}

// Fix SdlcRunService.GetActiveRunsAsync
var runIds = await _artifactStore.GetAllRunIdsAsync();
```

**Done when:** Dashboard query returns all runs that have artifacts.

---

## Phase 3 — Infrastructure Hardening

### 3.1 — Fix `ArtifactStore` File Path Collision on Re-runs

**File:** `SDLC/src/SDLC.Infrastructure/ArtifactStore.cs`

**Problem:** `ArtifactPath` maps `(RunId, Stage)` to a single file. Re-running a stage overwrites the previous artifact file without changing its DB path pointer.

**Fix:** Include `ArtifactId` in the path so each artifact version gets its own file:

```csharp
private string ArtifactPath(SdlcArtifact a) =>
    Path.Combine(_basePath, a.RunId.ToString(), $"{a.Stage}-{a.ArtifactId:N}.md");
```

**Impact:** Update `GetStageName` path lookups and `CreateArtifact` — these read `file_path` from DB so they're unaffected (the path is stored at save time). No migration needed for new runs; old runs will have old-format paths.

**Done when:** Integration test: save two `ResearchBrief` artifacts for same run; both files exist on disk; both retrievable by `ArtifactId`.

---

### 3.2 — Decouple `SdlcRunService` From Concrete `PipelineRunnerService`

**File:** `SDLC/src/SDLC.Dashboard/Services/SdlcRunService.cs`

**Problem:** Constructor takes `PipelineRunnerService` (concrete). Breaks DI substitution and testability beyond the current `TestRunner` subclass hack.

**Fix:** Extract an interface:

```csharp
// Add to SDLC/src/SDLC.Orchestrator/OrchestratorContracts.cs
public interface IPipelineRunner
{
    bool IsRunActive(Guid runId);
    Task EnqueueAsync(SdlcRunConfig config, CancellationToken ct = default);
    Task ResumeGateAsync(Guid runId, Guid gateId, GateDecision decision, string? notes, CancellationToken ct = default);
}
```

Make `PipelineRunnerService` implement `IPipelineRunner`. Change `SdlcRunService` constructor to take `IPipelineRunner`.

**Done when:** `SdlcRunService` tests use `IPipelineRunner` mock directly without subclassing the concrete class.

---

### 3.3 — Fix `UpdateContentAsync` Silent Status Reset

**File:** `SDLC/src/SDLC.Infrastructure/ArtifactStore.cs`

**Problem:** Editing artifact content silently resets status to `Draft` with no audit trail.

**Fix:** Make the status transition explicit and documented. Change the method signature to accept the target status, or always reset to `PendingReview` (not `Draft`) to require re-approval:

```csharp
public async Task UpdateContentAsync(Guid artifactId, string content)
{
    using var conn = new SqliteConnection(_connectionString);
    await conn.OpenAsync();

    var row = await conn.QueryFirstOrDefaultAsync<string>(
        "SELECT file_path FROM artifacts WHERE artifact_id = :Id",
        new { Id = artifactId.ToString() });

    if (row != null)
        await File.WriteAllTextAsync(row, content);

    // Content edited → needs re-approval; reset to PendingReview (not Draft)
    await conn.ExecuteAsync(
        "UPDATE artifacts SET status = 'PendingReview' WHERE artifact_id = :Id",
        new { Id = artifactId.ToString() });
}
```

**Done when:** Test: update content on an `Approved` artifact → status becomes `PendingReview`, not `Draft`.

---

### 3.4 — Fix `StageGateStore` `GateDecision`→`GateStatus` String Coupling

**File:** `SDLC/src/SDLC.Infrastructure/StageGateStore.cs`

**Problem:** `ResolveAsync` writes `decision.ToString()` as the status string. Works by accident because enum names match; fragile.

**Fix:** Explicitly map:

```csharp
public async Task ResolveAsync(Guid gateId, GateDecision decision, string? notes)
{
    var status = decision == GateDecision.Approved
        ? GateStatus.Approved.ToString()
        : GateStatus.Rejected.ToString();

    await using var conn = new Microsoft.Data.Sqlite.SqliteConnection(_connectionString);
    await conn.OpenAsync();
    await using var cmd = new Microsoft.Data.Sqlite.SqliteCommand(@"
        UPDATE gates SET status = @status, notes = @notes, resolved_at = @resolved_at
        WHERE gate_id = @id", conn);
    cmd.Parameters.AddWithValue("@status", status);
    // ... rest unchanged
}
```

**Done when:** `GateDecision` could be renamed without breaking gate status persistence.

---

### 3.5 — Handle Notification Failure Without Orphaning Gates

**File:** `SDLC/src/SDLC.Orchestrator/SdlcProcess.cs` — `StageGateStep.RequestApprovalAsync`

**Problem:** Gate is created in DB, then notification fails. Gate is orphaned — `Pending` forever, no reviewer notified.

**Fix:** If notification fails, mark the gate as failed and log. Do not propagate the exception as a pipeline crash — instead retry notification or fall back to a secondary channel.

```csharp
public async Task RequestApprovalAsync(
    IKernelProcessStepContext context,
    SdlcArtifact artifact,
    INotificationService notifications,
    IStageGateStore gateStore,
    ILogger<StageGateStep>? logger = null,
    CancellationToken ct = default)
{
    var gate = await gateStore.CreateGateAsync(artifact);

    try
    {
        await notifications.SendApprovalRequestAsync(gate);
    }
    catch (Exception ex)
    {
        logger?.LogError(ex, "Notification failed for gate {GateId}. Gate remains pending — review manually.", gate.GateId);
        // Gate still exists and is Pending. Dashboard can still show it.
        // Do NOT rethrow — allow pipeline to suspend on the gate.
    }

    await context.EmitEventAsync(new KernelProcessEvent
    {
        Id = SdlcEvents.GatePending,
        Data = gate.GateId
    }, ct);
}
```

**Done when:** Test updated: notification failure logs error but does not throw; gate is still created and pending event is emitted.

---

## Phase 4 — Notification Service

### 4.1 — Add `DashboardUrlBuilder`

**Create file:** `SDLC/src/SDLC.Notifications/DashboardUrlBuilder.cs`

```csharp
namespace SDLC.Notifications;

public class DashboardUrlBuilder
{
    private readonly string _baseUrl;

    public DashboardUrlBuilder(string baseUrl)
    {
        _baseUrl = baseUrl.TrimEnd('/');
    }

    public string ForGate(Guid gateId) => $"{_baseUrl}/gate/{gateId}";
}
```

Configure `_baseUrl` from `appsettings.json`: `Dashboard:BaseUrl`.

---

### 4.2 — Replace Slack Payload With Block Kit Message

**File:** `SDLC/src/SDLC.Notifications/INotificationService.cs`

**Problem:** Current payload is raw gate metadata. Slack expects Block Kit JSON. No review URL. No buttons.

**Fix:** Replace `SlackNotificationService.SendApprovalRequestAsync`:

```csharp
public class SlackNotificationService : INotificationService
{
    private readonly HttpClient _httpClient;
    private readonly string _webhookPath;
    private readonly DashboardUrlBuilder _urls;

    public SlackNotificationService(HttpClient httpClient, string webhookPath, DashboardUrlBuilder urls)
    {
        _httpClient = httpClient;
        _webhookPath = webhookPath;
        _urls = urls;
    }

    public async Task SendApprovalRequestAsync(StageGate gate)
    {
        var reviewUrl = _urls.ForGate(gate.GateId);

        var payload = new
        {
            text = $"SDLC Stage Gate — {gate.Stage} requires review",
            blocks = new object[]
            {
                new
                {
                    type = "section",
                    text = new { type = "mrkdwn", text = $"*SDLC Stage Gate — {gate.Stage}*" }
                },
                new
                {
                    type = "section",
                    text = new { type = "mrkdwn", text = $"Run `{gate.RunId}` requires review before proceeding to the next stage." }
                },
                new
                {
                    type = "actions",
                    elements = new object[]
                    {
                        new
                        {
                            type = "button",
                            text = new { type = "plain_text", text = "Review & Approve" },
                            url = reviewUrl,
                            style = "primary"
                        },
                        new
                        {
                            type = "button",
                            text = new { type = "plain_text", text = "Reject — Re-run Stage" },
                            url = reviewUrl,
                            style = "danger"
                        }
                    }
                }
            }
        };

        var response = await _httpClient.PostAsJsonAsync(_webhookPath, payload);
        response.EnsureSuccessStatusCode();
    }
}
```

**Note:** Slack Incoming Webhooks do not support `url` on buttons. To get button click callbacks you need Slack Interactivity (OAuth app with action handlers). For now, buttons link to the dashboard URL. Users click the link and approve/reject in the browser. Full Slack action handler integration is a future enhancement.

**Done when:** Notification test updated to verify `blocks` array and `url` in button element.

---

## Phase 5 — Blazor Dashboard

### 5.1 — Register Services in `Program.cs`

**File:** `SDLC/src/SDLC.Dashboard/Program.cs`

Replace the default Blazor template registration with full service wiring:

```csharp
using SDLC.Agents;
using SDLC.Contracts;
using SDLC.Dashboard.Components;
using SDLC.Dashboard.Services;
using SDLC.Infrastructure;
using SDLC.Notifications;
using SDLC.Orchestrator;

var builder = WebApplication.CreateBuilder(args);

// Storage
var connStr = builder.Configuration.GetConnectionString("ArtifactDb")
    ?? "Data Source=artifacts.db";
var fsPath = builder.Configuration["ArtifactStore:BasePath"] ?? "artifacts";

builder.Services.AddSingleton<IArtifactStore>(sp =>
{
    var store = new ArtifactStore(connStr, fsPath);
    store.InitializeAsync().GetAwaiter().GetResult();
    return store;
});
builder.Services.AddSingleton<IStageGateStore>(sp =>
{
    var store = new StageGateStore(connStr);
    store.InitializeAsync().GetAwaiter().GetResult();
    return store;
});

// Model routing (loaded from config or use defaults)
builder.Services.AddSingleton(ModelRoutingConfig.Default);
builder.Services.AddSingleton<IKernelFactory, AgentKernelFactory>();

// Notifications
builder.Services.AddSingleton(new DashboardUrlBuilder(
    builder.Configuration["Dashboard:BaseUrl"] ?? "http://localhost:5200"));
builder.Services.AddHttpClient<INotificationService, SlackNotificationService>((sp, client) =>
{
    client.BaseAddress = new Uri(builder.Configuration["Slack:WebhookBaseUrl"] ?? "https://hooks.slack.com");
});

// Orchestrator
builder.Services.AddSingleton<SdlcProcessFactory>();
builder.Services.AddSingleton<ISdlcProcessFactory>(sp => sp.GetRequiredService<SdlcProcessFactory>());
builder.Services.AddSingleton<PipelineRunnerService>();
builder.Services.AddSingleton<IPipelineRunner>(sp => sp.GetRequiredService<PipelineRunnerService>());

// Dashboard service
builder.Services.AddScoped<ISdlcRunService, SdlcRunService>();

// Blazor
builder.Services.AddRazorComponents().AddInteractiveServerComponents();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseAntiforgery();
app.MapStaticAssets();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

app.Run();
```

**Done when:** App starts without DI exceptions; `/` loads.

---

### 5.2 — Create Blazor Pages

**Files to create** (all under `SDLC/src/SDLC.Dashboard/Components/Pages/`):

#### `Runs/Index.razor` — Run list

```razor
@page "/runs"
@inject ISdlcRunService RunService
@rendermode InteractiveServer

<h1>Active Runs</h1>

@if (_runs is null)
{
    <p>Loading...</p>
}
else if (!_runs.Any())
{
    <p>No active runs. <a href="/runs/new">Start one.</a></p>
}
else
{
    <table>
        <thead><tr><th>Run ID</th><th>Last Stage</th><th>Pending Gates</th><th></th></tr></thead>
        <tbody>
        @foreach (var run in _runs)
        {
            <tr>
                <td><code>@run.RunId</code></td>
                <td>@run.LastStage</td>
                <td>@run.PendingGates.Count</td>
                <td><a href="/runs/@run.RunId">View</a></td>
            </tr>
        }
        </tbody>
    </table>
}

@code {
    private IReadOnlyList<RunSummary>? _runs;

    protected override async Task OnInitializedAsync() =>
        _runs = await RunService.GetActiveRunsAsync();
}
```

#### `Runs/NewRun.razor` — Start a run

```razor
@page "/runs/new"
@inject ISdlcRunService RunService
@inject NavigationManager Nav
@rendermode InteractiveServer

<h1>New SDLC Run</h1>

<label>Project Brief</label>
<textarea @bind="_brief" rows="8" style="width:100%"></textarea>

<button @onclick="StartAsync">Start Run</button>

@if (!string.IsNullOrEmpty(_error))
{
    <p style="color:red">@_error</p>
}

@code {
    private string _brief = "";
    private string? _error;

    private async Task StartAsync()
    {
        if (string.IsNullOrWhiteSpace(_brief)) { _error = "Brief is required."; return; }
        var runId = await RunService.StartRunAsync(new SdlcRunConfig { ProjectBrief = _brief });
        Nav.NavigateTo($"/runs/{runId}");
    }
}
```

**Note:** `ISdlcRunService` needs a `StartRunAsync(SdlcRunConfig)` method added (see 5.3).

#### `Runs/RunDetail.razor` — Per-run view

```razor
@page "/runs/{RunId:guid}"
@inject ISdlcRunService RunService
@rendermode InteractiveServer

<h1>Run @RunId</h1>

@if (_detail is null)
{
    <p>Loading...</p>
}
else
{
    <p>Status: @(_detail.IsActive ? "Running" : "Complete") | Last Stage: @_detail.LastStage</p>

    <h2>Artifacts</h2>
    <ul>
    @foreach (var a in _detail.Artifacts)
    {
        <li>@a.Stage — @a.TypeName — @a.Status (@a.CreatedAt.ToLocalTime():g)</li>
    }
    </ul>

    <h2>Pending Gates</h2>
    @if (!_detail.AllGates.Any(g => g.Status == GateStatus.Pending))
    {
        <p>None.</p>
    }
    @foreach (var g in _detail.AllGates.Where(g => g.Status == GateStatus.Pending))
    {
        <p><a href="/gate/@g.GateId">Review gate for @g.Stage</a></p>
    }
}

@code {
    [Parameter] public Guid RunId { get; set; }
    private RunDetail? _detail;

    protected override async Task OnInitializedAsync() =>
        _detail = await RunService.GetRunDetailAsync(RunId);
}
```

#### `StageGate/Review.razor` — HITL approval page

This is the page linked from Slack notifications.

```razor
@page "/gate/{GateId:guid}"
@inject ISdlcRunService RunService
@rendermode InteractiveServer

<h1>Stage Gate Review — @_gate?.Stage</h1>

@if (_gate is null)
{
    <p>Gate not found or already resolved.</p>
}
else if (_gate.Status != GateStatus.Pending)
{
    <p>This gate has already been @_gate.Status.</p>
}
else
{
    <p>Run: <code>@_gate.RunId</code> | Stage: @_gate.Stage</p>

    <h2>Artifact Content</h2>
    <textarea @bind="_editableContent" rows="30" style="width:100%; font-family:monospace"></textarea>

    <label>Notes</label>
    <input @bind="_notes" style="width:100%" />

    @if (!string.IsNullOrEmpty(_error))
    {
        <p style="color:red">@_error</p>
    }

    <button style="background:green;color:white" @onclick="ApproveAsync">✓ Approve</button>
    <button style="background:red;color:white"   @onclick="RejectAsync">✗ Reject — Re-run Stage</button>
}

@code {
    [Parameter] public Guid GateId { get; set; }
    private GateSummary? _gate;
    private string _editableContent = "";
    private string _notes = "";
    private string? _error;

    protected override async Task OnInitializedAsync()
    {
        var detail = await RunService.GetGateDetailAsync(GateId);
        _gate = detail;
        _editableContent = detail?.Notes ?? "";
    }

    private async Task ApproveAsync()
    {
        try
        {
            await RunService.ApproveGateAsync(GateId, _notes);
        }
        catch (Exception ex) { _error = ex.Message; }
    }

    private async Task RejectAsync()
    {
        if (string.IsNullOrWhiteSpace(_notes)) { _error = "Notes required when rejecting."; return; }
        try
        {
            await RunService.RejectGateAsync(GateId, _notes);
        }
        catch (Exception ex) { _error = ex.Message; }
    }
}
```

**Note:** `GetGateDetailAsync` needs to be added to `ISdlcRunService` (see 5.3).

---

### 5.3 — Extend `ISdlcRunService`

**File:** `SDLC/src/SDLC.Dashboard/Services/SdlcRunService.cs`

Add:
```csharp
// To interface
Task<Guid> StartRunAsync(SdlcRunConfig config, CancellationToken ct = default);
Task<GateSummary?> GetGateDetailAsync(Guid gateId, CancellationToken ct = default);

// Implementation
public async Task<Guid> StartRunAsync(SdlcRunConfig config, CancellationToken ct = default)
{
    await _runner.EnqueueAsync(config, ct);
    return config.RunId;
}

public async Task<GateSummary?> GetGateDetailAsync(Guid gateId, CancellationToken ct = default)
{
    var gate = await _gateStore.GetAsync(gateId);
    if (gate is null) return null;
    return new GateSummary(gate.GateId, gate.Stage, gate.Status, gate.Notes);
}
```

---

## Phase 6 — Observability

### 6.1 — Wire `IPipelineTelemetry` Into Steps

**Problem:** `IPipelineTelemetry` is implemented and tested but never called from `ResearchStep`, `BuildStep`, or `PipelineRunnerService`.

**Files:** `SDLC/src/SDLC.Agents/ResearchStep.cs`, `BuildStep.cs`, `SdlcProcess.cs`

**Fix:** Add `IPipelineTelemetry` parameter to each step's `RunAsync` and call:
- `RecordStepCompletedAsync` on success
- `RecordStepFailedAsync` in a catch block
- `StartPipelineRunAsync` in `EnqueueAsync`
- `CompletePipelineRunAsync` in the pipeline continuation

---

### 6.2 — Add Real OTel SDK Integration

**Files:** `SDLC/src/SDLC.Dashboard/Program.cs`, `SDLC/src/SDLC.Orchestrator/SdlcProcess.cs`

Add NuGet packages to Orchestrator and Dashboard csproj:
```xml
<PackageReference Include="OpenTelemetry.Extensions.Hosting" Version="1.*" />
<PackageReference Include="OpenTelemetry.Exporter.OpenTelemetryProtocol" Version="1.*" />
<PackageReference Include="OpenTelemetry.Instrumentation.Http" Version="0.*" />
<PackageReference Include="OpenTelemetry.Instrumentation.AspNetCore" Version="1.*" />
```

Add to `Program.cs`:
```csharp
var otlpEndpoint = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"] ?? "http://localhost:18889";

builder.Services.AddOpenTelemetry()
    .WithTracing(t => t
        .AddSource("SDLC.*")
        .AddHttpClientInstrumentation()
        .AddAspNetCoreInstrumentation()
        .AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint)))
    .WithMetrics(m => m
        .AddMeter("SDLC.*")
        .AddRuntimeInstrumentation()
        .AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint)));
```

**Create file:** `SDLC/src/SDLC.Telemetry/SdlcTelemetry.cs`

```csharp
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace SDLC.Telemetry;

public static class SdlcTelemetry
{
    private static readonly ActivitySource Source = new("SDLC.Pipeline");
    private static readonly Meter Meter = new("SDLC.Pipeline");

    public static readonly Counter<long> RunsStarted   = Meter.CreateCounter<long>("sdlc.runs.started");
    public static readonly Counter<long> RunsCompleted = Meter.CreateCounter<long>("sdlc.runs.completed");
    public static readonly Counter<long> GatesApproved = Meter.CreateCounter<long>("sdlc.gates.approved");
    public static readonly Counter<long> GatesRejected = Meter.CreateCounter<long>("sdlc.gates.rejected");
    public static readonly Histogram<double> StageDuration =
        Meter.CreateHistogram<double>("sdlc.stage.duration_ms");

    public static Activity? StartRunActivity(Guid runId) =>
        Source.StartActivity("SdlcPipeline.Run")?.SetTag("run.id", runId);

    public static Activity? StartStageActivity(Guid runId, string stage) =>
        Source.StartActivity($"SdlcPipeline.{stage}")?.SetTag("run.id", runId);

    public static Activity? StartBuildActivity(Guid runId) =>
        Source.StartActivity("SdlcPipeline.Build")
              ?.SetTag("run.id", runId)
              ?.SetTag("sweaf.trigger", true);
}
```

**Done when:** Traces appear in Aspire Dashboard when running a pipeline.

---

## Phase 7 — Docker / Deployment

### 7.1 — Create `docker-compose.yml`

**Create file:** `SDLC/docker/docker-compose.yml`

```yaml
services:

  aspire-dashboard:
    image: mcr.microsoft.com/dotnet/aspire-dashboard:latest
    ports:
      - "18888:18888"
      - "18889:18889"
    environment:
      DASHBOARD__OTLP__AUTHMODE: Unsecured

  orchestrator:
    build:
      context: ../src/SDLC.Orchestrator
      dockerfile: Dockerfile
    environment:
      OTEL_EXPORTER_OTLP_ENDPOINT: http://aspire-dashboard:18889
      SWEAF__BaseUrl: http://host.docker.internal:5100
      ConnectionStrings__ArtifactDb: Data Source=/data/artifacts.db
      ArtifactStore__BasePath: /data/artifacts
      Notifications__Slack__WebhookBaseUrl: https://hooks.slack.com
      Notifications__Slack__WebhookPath: ${SLACK_WEBHOOK_PATH}
      Dashboard__BaseUrl: http://localhost:5200
    volumes:
      - artifact-data:/data
    depends_on:
      - aspire-dashboard

  dashboard:
    build:
      context: ../src/SDLC.Dashboard
      dockerfile: Dockerfile
    ports:
      - "5200:8080"
    environment:
      OTEL_EXPORTER_OTLP_ENDPOINT: http://aspire-dashboard:18889
      ConnectionStrings__ArtifactDb: Data Source=/data/artifacts.db
      ArtifactStore__BasePath: /data/artifacts
      Dashboard__BaseUrl: http://localhost:5200
    volumes:
      - artifact-data:/data
    depends_on:
      - orchestrator

volumes:
  artifact-data:
```

### 7.2 — Create Dockerfiles

**Create:** `SDLC/src/SDLC.Dashboard/Dockerfile`
**Create:** `SDLC/src/SDLC.Orchestrator/Dockerfile`

Both follow the standard .NET multi-stage pattern:
```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["SDLC.Dashboard.csproj", "."]
RUN dotnet restore
COPY . .
RUN dotnet publish -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "SDLC.Dashboard.dll"]
```

---

## Phase 8 — Test Coverage Gaps

The following scenarios have no tests and should be added:

| Gap | Test Project | What to Test |
|---|---|---|
| `ResearchStep` fallback uses AI response not critique | `SDLC.Agents.Tests` | All 3 attempts unsatisfactory → `brief.Content == lastAiResponse` |
| `BuildStep` null result guard | `SDLC.Agents.Tests` | Empty poll stream → saves `BuildResult` with `Success=false` |
| `UpdateContentAsync` sets `PendingReview` not `Draft` | `SDLC.Infrastructure.Tests` | Update content on `Approved` artifact → status is `PendingReview` |
| `GetActiveRunsAsync` returns all runs | `SDLC.Dashboard.Tests` | After saving 3 runs' artifacts → service returns 3 summaries |
| Gate suspension blocks until resume | `SDLC.Orchestrator.Tests` | `WaitForGateAsync` unblocks after `ResumeGateAsync` |
| `StageGateStep` notification failure doesn't orphan gate | `SDLC.Orchestrator.Tests` | Notification throws → gate still pending, no rethrow |
| `RequirementsStep` full cycle | `SDLC.Agents.Tests` | Mirrors `ResearchStepTests` |
| `DesignStep` full cycle | `SDLC.Agents.Tests` | Mirrors `ResearchStepTests` |
| `LearnStep` full cycle | `SDLC.Agents.Tests` | Mirrors `ResearchStepTests` |
| `ArtifactStore` no file path collision on re-run | `SDLC.Infrastructure.Tests` | Save 2 `ResearchBrief` same run → 2 distinct files on disk |
| Full pipeline end-to-end | `SDLC.Integration.Tests` | Enqueue run → all 5 stages complete → `LearnReport` persisted |

---

## Completion Checklist

```
Phase 0 — Blockers
  [ ] 0.1  StageGateStore implements IStageGateStore
  [ ] 0.2  Class1.cs stubs deleted
  [ ] 0.3  SDLC.sln verified / generated

Phase 1 — Real AI Execution
  [ ] 1.1  DefaultKernel makes real HTTP call to vLLM
  [ ] 1.2  ResearchStep fallback bug fixed
  [ ] 1.3  RequirementsStep created + tested
  [ ] 1.4  DesignStep created + tested
  [ ] 1.5  LearnStep created + tested
  [ ] 1.6  BuildStep null guard added

Phase 2 — Pipeline Wiring
  [ ] 2.1  SdlcProcessFactory implemented
  [ ] 2.2  EnqueueAsync calls factory
  [ ] 2.3  Gate suspension / resume with TaskCompletionSource
  [ ] 2.4  GetActiveRunsAsync fixed (real run ID query)

Phase 3 — Infrastructure Hardening
  [ ] 3.1  ArtifactStore file path includes ArtifactId
  [ ] 3.2  SdlcRunService uses IPipelineRunner interface
  [ ] 3.3  UpdateContentAsync sets PendingReview not Draft
  [ ] 3.4  GateDecision→GateStatus explicit mapping
  [ ] 3.5  Notification failure handled without orphaning gate

Phase 4 — Notifications
  [ ] 4.1  DashboardUrlBuilder created
  [ ] 4.2  Slack Block Kit payload with review URL

Phase 5 — Blazor Dashboard
  [ ] 5.1  Program.cs full DI wiring
  [ ] 5.2  Blazor pages: Index, NewRun, RunDetail, Review
  [ ] 5.3  ISdlcRunService extended with StartRunAsync, GetGateDetailAsync

Phase 6 — Observability
  [ ] 6.1  IPipelineTelemetry wired into all steps
  [ ] 6.2  OTel SDK registered; traces export to Aspire Dashboard

Phase 7 — Docker
  [ ] 7.1  docker-compose.yml created
  [ ] 7.2  Dockerfiles for Dashboard and Orchestrator

Phase 8 — Tests
  [ ] All gaps in table above covered
  [ ] dotnet test passes 136+ tests (all existing + new)
```

---

## Key Architectural Note for Implementing Agents

The current codebase **has no SK Process Framework** (`Microsoft.SemanticKernel.Process.Runtime.InProcess`). Phases 1–5 above implement a working pipeline using custom interfaces (`IKernelProcessStepContext`, `IProcessRuntime`, etc.) that already exist in the codebase. This is deliberate — it keeps the system testable and removes the dependency on SK Process internals.

If SK Process Framework integration is desired later, the swap is surgical: replace `SdlcProcessFactory.RunPipelineAsync` internals with `ProcessBuilder`/`KernelProcessStep` while keeping all surrounding contracts, storage, notification, and dashboard code unchanged.

Do not refactor the custom interfaces to use SK types until Phase 1–5 is working end-to-end.
