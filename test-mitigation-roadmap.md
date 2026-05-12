# Test Coverage Mitigation Roadmap

## Current State

| Metric | Value |
|---|---|
| Source projects | 8 |
| Test projects | 8 (1:1 mapping) |
| Test files | 56 |
| Integration tests | 2 real + 1 placeholder |
| E2E tests | 0 |
| Test framework | NUnit 4.6 + FluentAssertions 8.9 + NSubstitute 5.3 |
| Coverage tooling | coverlet.collector included, no thresholds, no reportgenerator |

---

## Phase 1 — Cleanup (1h)

Delete the 3 placeholder UnitTest1.cs files. They assert true, test nothing, and pollish coverage reports.

| File | Line |
|---|---|
| `SDLC/tests/SDLC.Integration.Tests/UnitTest1.cs` | 16 |
| `SDLC/tests/SDLC.Orchestrator.Tests/UnitTest1.cs` | 16 |
| `SDLC/tests/SDLC.Dashboard.Tests/UnitTest1.cs` | 16 |

Each contains `class Tests` with `void Test1()` calling `Assert.Pass()`. Delete the file, remove the `<Compile Include="UnitTest1.cs">` reference from the .csproj if present.

---

## Phase 2 — High-Risk Untested Code (priority order)

### 2.1 SdlcProcessFactory.cs — Orchestrator (very high risk)

**File:** `SDLC/src/SDLC.Orchestrator/SdlcProcessFactory.cs` (231 lines)
**Test project:** `SDLC.Orchestrator.Tests`
**No test class exists.**

This is the most critical gap. It orchestrates the full 5-stage pipeline with gate waits, resume logic, error handling, and budget tracking.

**Tests to write:**

1. **`SdlcProcessFactoryPipelineTests.cs`** — mock all dependencies, verify `RunPipelineAsync` calls stages in correct order (Research -> Requirements -> Gate -> Design -> Gate -> Build -> Learn), updates run store stages correctly.

2. **`SdlcProcessFactoryResumeTests.cs`** — verify `ResumePipelineAsync` branches on stage param:
   - Resume at "Research": loads existing artifacts if present, runs missing stages
   - Resume at "Design": loads Research + Requirements, runs Design
   - Resume at "Build": loads all prior artifacts, runs Build
   - Resume at "Learn": loads all prior artifacts, runs Build + Learn (rebuilds if missing)
   - Unknown stage throws `InvalidOperationException`

3. **`SdlcProcessFactoryGateTests.cs`** — verify `WaitForGateWithApprovalAsync`:
   - Creates gate, sends notification, waits for resolution
   - Approved gate: continues pipeline
   - Rejected gate: throws `GateRejectedException`
   - Notification failure: logs error, gate remains pending (does not abort)

4. **`SdlcProcessFactoryErrorHandlingTests.cs`** — verify pipeline failure path:
   - Exception in any stage: logs error, updates run store to "Failed", re-throws
   - Budget exceeded during pipeline

5. **`ProcessHandleTests.cs`** — verify `ProcessHandle.Task` returns the underlying task, await semantics work.

---

### 2.2 ResilientHttpClientFactory.cs — Agents (high risk)

**File:** `SDLC/src/SDLC.Agents/ResilientHttpClientFactory.cs` (106 lines)
**Test project:** `SDLC.Agents.Tests`
**No test class exists.**

Non-trivial Polly pipeline builder. Configures per-stage retry/backoff/timeout + auth headers.

**Tests to write:**

1. **`ResilientHttpClientFactoryTests.cs`** — verify `CreateForStage`:
   - Returns non-null `HttpClient` for each `SdlcStage`
   - `HttpClient.Timeout` matches endpoint config or 3-min default
   - Bearer token set when `ModelRoutingConfig` has non-empty `ApiKey`
   - Bearer token NOT set when ApiKey is empty
   - Each call returns distinct `HttpClient` instance

2. **`ResilienceHandlerTests.cs`** — verify `SendAsync` composes retry around timeout:
   - Transient error (502/503): retries correct number of times
   - Rate limit (429): retries
   - Success on first try: no retry logging
   - Timeout policy fires: throws `TimeoutRejectedException`

3. **`ResilienceHandlerLoggingTests.cs`** — verify retry logging includes retry count, stage name, status.

4. **`StageResilienceConfigTests.cs`** — verify per-stage retry/backoff values:
   - Research/Requirements: 3 retries, 1000ms base
   - Design: 2 retries, 1500ms base
   - Build: 4 retries, 2000ms base
   - Learn: 3 retries, 1000ms base

---

### 2.3 SweAfClient.cs — Agents (medium risk)

**File:** `SDLC/src/SDLC.Agents/SweAfClient.cs` (42 lines)
**Test project:** `SDLC.Agents.Tests`

HTTP client for SWE-AF integration.

**Tests to write:**

1. **`SweAfClientTests.cs`** — use `HttpClient` with `HttpMessageHandler` mock or `HttpMocker`:
   - `SubmitAsync`: POSTs to `/api/runs`, returns run ID from JSON response
   - `SubmitAsync`: throws `InvalidOperationException` on empty response
   - `SubmitAsync`: propagates HTTP error via `EnsureSuccessStatusCode`
   - `PollAsync`: returns status from JSON, polls repeatedly until `IsTerminal`
   - `PollAsync`: throws on empty poll response
   - `PollAsync`: respects cancellation token

---

## Phase 3 — Medium-Risk Untested Code

### 3.1 RateLimiter.cs — Dashboard

**File:** `SDLC/src/SDLC.Dashboard/Services/RateLimiter.cs` (55 lines)
**Test project:** `SDLC.Dashboard.Tests`

Token-bucket-style rate limiter with concurrent dictionary and lock.

**Tests to write — `RateLimiterTests.cs`:**

1. `Allow()` returns true when under limit
2. `Allow()` returns false when over limit
3. Window expiry resets count
4. Multiple keys are independent
5. `Sweep()` removes expired windows
6. `Sweep()` preserves non-expired windows

---

### 3.2 RunStateHub.cs — Dashboard

**File:** `SDLC/src/SDLC.Dashboard/Hubs/RunStateHub.cs` (13 lines)
**Test project:** `SDLC.Dashboard.Tests`

SignalR hub — thin wrapper around `AddToGroupAsync`/`RemoveFromGroupAsync`.

**Tests to write — `RunStateHubTests.cs`:**

1. `SubscribeToRun` calls `AddToGroupAsync` with correct runId string
2. `UnsubscribeFromRun` calls `RemoveFromGroupAsync` with correct runId string

---

### 3.3 TelemetryController.cs — Dashboard

**File:** `SDLC/src/SDLC.Dashboard/Controllers/TelemetryController.cs` (19 lines)
**Test project:** `SDLC.Dashboard.Tests`

ASP.NET Core controller — 3 endpoints, each delegates to `IPipelineTelemetry`.

**Tests to write — `TelemetryControllerTests.cs`:**

1. `StepsAsync` returns 200 with `IReadOnlyList<StepEvent>` from telemetry
2. `GatesAsync` returns 200 with `IReadOnlyList<GateEvent>`
3. `PipelinesAsync` returns 200 with `IReadOnlyList<PipelineEvent>`
4. Null telemetry instance returns 500 (or whatever ASP.NET does)

---

### 3.4 HistoryTruncator.cs — Agents

**File:** `SDLC/src/SDLC.Agents/HistoryTruncator.cs` (19 lines)
**Test project:** `SDLC.Agents.Tests`

Simple static method: truncates conversation history to max turns with system prompt preservation.

**Tests to write — `HistoryTruncatorTests.cs`:**

1. Under limit: returns input unchanged
2. Exactly at limit + 1: returns input unchanged
3. Over limit + 1 but under double limit: returns input unchanged
4. Over double limit: truncates to last 2*maxTurns entries, prepends system prompt (index 0)
5. Empty input: returns empty
6. Single element: returns single element

---

### 3.5 PipelineRecoveryHostedService.cs — Orchestrator

**File:** `SDLC/src/SDLC.Orchestrator/PipelineRecoveryHostedService.cs` (25 lines)
**Test project:** `SDLC.Orchestrator.Tests`

IHostedService that calls `PipelineRunnerService.RecoverPendingGatesAsync()` with error handling.

**Tests to write — `PipelineRecoveryHostedServiceTests.cs`:**

1. `StartAsync` calls `RecoverPendingGatesAsync()` on runner
2. `StartAsync` catches exceptions and logs error (does not re-throw)
3. `StopAsync` returns `Task.CompletedTask`

---

### 3.6 SdlcTelemetry.cs — Telemetry

**File:** `SDLC/src/SDLC.Telemetry/SdlcTelemetry.cs` (39 lines)
**Test project:** `SDLC.Telemetry.Tests`

Static facade with Meter/ActivitySource + proxy to `IPipelineTelemetry` instance.

**Tests to write — `SdlcTelemetryFacadeTests.cs`:**

1. `Meter` is non-null with name "SDLC.Pipeline"
2. `ActivitySource` is non-null with name "SDLC.Pipeline"
3. All counters are non-null (6 counters)
4. `StageDuration` histogram is non-null
5. `RecordStepCompleted` delegates to `Instance?.RecordStepCompletedAsync()` when Instance is set
6. `RecordStepCompleted` does NOT throw when Instance is null
7. `RecordTokenUsage` calls `Add()` on both token counters with correct values

---

## Phase 4 — Low-Risk Trivial Code

### 4.1 DashboardUrlBuilder.cs — Notifications

**File:** `SDLC/src/SDLC.Notifications/DashboardUrlBuilder.cs` (7 lines)
**Test project:** `SDLC.Notifications.Tests`

One-line URL construction.

**Tests — `DashboardUrlBuilderTests.cs`:**

1. `ForGate` with trailing slash in baseUrl: `"http://localhost/gate/{id}"`
2. `ForGate` without trailing slash: same result
3. `ForGate` produces valid URL format

---

### 4.2 FallbackEmailNotificationService.cs — Notifications

**File:** `SDLC/src/SDLC.Notifications/FallbackEmailNotificationService.cs` (23 lines)
**Test project:** `SDLC.Notifications.Tests`

Logs warning, returns completed task. No behavior to test beyond null reference.

**Tests — `FallbackEmailNotificationServiceTests.cs`:**

1. `SendApprovalRequestAsync` returns completed task (it already does)
2. Logs a warning (verify via captured logger)

---

## Phase 5 — Integration Tests

### 5.1 Delete placeholder

**File:** `SDLC/tests/SDLC.Integration.Tests/UnitTest1.cs`
Contains `Assert.Pass()`. Delete.

### 5.2 Full pipeline integration test

**New file:** `SDLC/tests/SDLC.Integration.Tests/FullPipelineIntegrationTests.cs`

Test the complete pipeline from Research through Learn using real `ArtifactStore` and `StageGateStore` (SQLite in temp file), with stubbed agent steps.

**Tests to write:**

1. **Happy path** — all 5 stages complete, gates auto-approved (via stub), run store shows Completed
2. **Gate rejection** — gate rejected mid-pipeline, pipeline aborts, run store shows Failed
3. **Concurrent runs** — two runs execute in parallel, artifacts and gates are isolated by runId
4. **Budget exceeded** — budget tracker throws, pipeline aborts, error handled gracefully

**Key setup:**
- Use `TestSQLiteConnection` or temp file SQLite databases (like `ArtifactAndGatePipelineTests` does)
- Stub `IKernelFactory` to return a mock kernel that emits synthetic step events
- Stub `ISweAfClient` to return a synthetic build result
- Use `InMemoryNotificationService` fake that auto-approves gates

---

### 5.3 SWE-AF HTTP integration test

**New file:** `SDLC/tests/SDLC.Integration.Tests/SweAfClientIntegrationTests.cs`

Real HTTP calls to a mock server or test endpoint.

**Tests to write:**

1. Submit task to mock server, receive run ID
2. Poll until terminal status
3. Timeout/cancellation during poll
4. Server returns 500 — client propagates error

**Setup:** Use `TestServer` (Microsoft.AspNetCore.Mvc.Testing) or `WebMocker` to create an in-process mock SWE-AF server.

---

## Phase 6 — Infrastructure

### 6.1 Coverage reporting

**Add to test projects or add a global `.runsettings` file:**

1. Install `coverlet.msbuild` (already present as `coverlet.collector`)
2. Install `dotnet-reportgenerator-globaltool` as a dotnet global tool (add to `dotnet-tools.json`)
3. Add a `Directory.Build.props` at solution root (or `SDLC/` level) with:
   ```xml
   <PropertyGroup>
     <CollectCoverage>true</CollectCoverage>
     <CoverletOutputFormat>cobertura</CoverletOutputFormat>
     <CoverletOutput>$(MSBuildThisFileDirectory)coverage\coverage.cobertura.xml</CoverletOutput>
     <ContinueOnCoverletError>true</ContinueOnCoverletError>
   </PropertyGroup>
   ```
4. Add coverage threshold (e.g., 70% branch coverage minimum)

### 6.2 Fix inconsistent Analyzer config

**File:** `SDLC/tests/SDLC.Telemetry.Tests/SDLC.Telemetry.Tests.csproj` (line 18)

Change:
```xml
<PackageReference Include="NUnit.Analyzers" Version="4.6.0" />
```
To:
```xml
<PackageReference Include="NUnit.Analyzers" Version="4.6.0" PrivateAssets="all" />
```
And add `buildtransitive`:
```xml
<PackageReference Include="NUnit.Analyzers" Version="4.6.0">
  <PrivateAssets>all</PrivateAssets>
  <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
</PackageReference>
```

### 6.3 Uncomment disabled test

**File:** `SDLC/tests/SDLC.Notifications.Tests/SlackNotificationServiceTests.cs` (lines 90-101)

The `SendApprovalRequestAsync_IncludesNotesInPayload` test is commented out with note "We removed notes from the payloads for now." Either:
- Remove the comment entirely if the feature was permanently removed
- Update the test if notes are re-added later

---

## Execution Order

| Phase | Files to Create | Files to Delete | Estimated Effort |
|---|---|---|---|
| 1. Cleanup | 0 | 3 UnitTest1.cs | 15 min |
| 2.1 SdlcProcessFactory | 1-5 test files | 0 | 4-6h |
| 2.2 ResilientHttpClientFactory | 1-4 test files | 0 | 2-3h |
| 2.3 SweAfClient | 1 test file | 0 | 1-2h |
| 3.1 RateLimiter | 1 test file | 0 | 1h |
| 3.2 RunStateHub | 1 test file | 0 | 30min |
| 3.3 TelemetryController | 1 test file | 0 | 30min |
| 3.4 HistoryTruncator | 1 test file | 0 | 30min |
| 3.5 PipelineRecoveryHostedService | 1 test file | 0 | 30min |
| 3.6 SdlcTelemetry | 1 test file | 0 | 1h |
| 4.1 DashboardUrlBuilder | 1 test file | 0 | 15min |
| 4.2 FallbackEmailNotificationService | 1 test file | 0 | 15min |
| 5.1 Delete placeholder | 0 | 1 UnitTest1.cs | 5 min |
| 5.2 FullPipelineIntegration | 1 test file | 0 | 3-4h |
| 5.3 SweAfClientIntegration | 1 test file | 0 | 2h |
| 6.1 Coverage reporting | Directory.Build.props | 0 | 1h |
| 6.2 Analyzer fix | 0 | 0 | 15min |
| 6.3 Uncomment disabled test | 0 | 0 | 10min |

**Total estimated effort: ~18-25 hours**

---

## Notes for Implementation

- Use NSubstitute for mocking (already available in all test projects)
- Use FluentAssertions for assertions (already available in all test projects)
- For HTTP tests, prefer `HttpMessageHandler` mock over spinning up real servers where possible
- For integration tests, follow the pattern in `ArtifactAndGatePipelineTests.cs` (temp SQLite files, real stores)
- All tests target `net10.0` — do not change target framework
- Keep test classes in the same namespace as the source they test
- Name test methods with pattern: `MethodName_Scenario_ExpectedResult` (snake_case, existing convention)
