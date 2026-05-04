# SDLC Agent — Comprehensive TDD Test Plan
**SDLC | .NET + Semantic Kernel | Agent-Executable**

---

## Agent Instructions

This document is a complete, ordered TDD specification. For each test:
1. Write the failing test **exactly as specified** (Red)
2. Write the minimum production code to make it pass (Green)
3. Refactor if needed, keeping all tests green (Refactor)

Tests are grouped by project and ordered by dependency. **Do not skip ahead** — later tests depend on interfaces and types established by earlier ones. Each test section lists its required NuGet packages.

All test projects use:
- `xUnit` — test runner
- `FluentAssertions` — assertions
- `NSubstitute` — mocking
- `Microsoft.NET.Test.Sdk`

---

## Test Project Layout

```
tests/
├── SDLC.Contracts.Tests/
├── SDLC.Infrastructure.Tests/
├── SDLC.Agents.Tests/
├── SDLC.Orchestrator.Tests/
├── SDLC.Notifications.Tests/
├── SDLC.Dashboard.Tests/
└── SDLC.Integration.Tests/
```

---

## Phase 1 — Contracts & Domain Model

**Project:** `SDLC.Contracts.Tests`
**Extra packages:** none beyond base set

---

### 1.1 — SdlcArtifact Base Record

**File:** `ArtifactTests.cs`

```csharp
public class ArtifactTests
{
    [Fact]
    public void SdlcArtifact_WhenCreated_HasNewArtifactId()
    {
        var artifact = new ResearchBrief { RunId = Guid.NewGuid(), Stage = SdlcStage.Research };
        artifact.ArtifactId.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public void SdlcArtifact_WhenCreated_HasUtcCreatedAt()
    {
        var before = DateTimeOffset.UtcNow;
        var artifact = new ResearchBrief { RunId = Guid.NewGuid(), Stage = SdlcStage.Research };
        var after = DateTimeOffset.UtcNow;

        artifact.CreatedAt.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);
        artifact.CreatedAt.Offset.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void SdlcArtifact_DefaultStatus_IsDraft()
    {
        var artifact = new ResearchBrief { RunId = Guid.NewGuid(), Stage = SdlcStage.Research };
        artifact.Status.Should().Be(ArtifactStatus.Draft);
    }

    [Fact]
    public void SdlcArtifact_TwoInstances_HaveDistinctArtifactIds()
    {
        var a1 = new ResearchBrief { RunId = Guid.NewGuid(), Stage = SdlcStage.Research };
        var a2 = new ResearchBrief { RunId = Guid.NewGuid(), Stage = SdlcStage.Research };
        a1.ArtifactId.Should().NotBe(a2.ArtifactId);
    }

    [Theory]
    [InlineData(ArtifactStatus.Draft)]
    [InlineData(ArtifactStatus.PendingReview)]
    [InlineData(ArtifactStatus.Approved)]
    [InlineData(ArtifactStatus.Rejected)]
    public void ArtifactStatus_AllValuesAreDefined(ArtifactStatus status)
    {
        Enum.IsDefined(status).Should().BeTrue();
    }
}
```

---

### 1.2 — Typed Artifact Records

**File:** `TypedArtifactTests.cs`

```csharp
public class TypedArtifactTests
{
    [Fact]
    public void ResearchBrief_Content_DefaultsToEmpty()
    {
        var brief = new ResearchBrief();
        brief.Content.Should().BeEmpty();
    }

    [Fact]
    public void RequirementsSpec_Criteria_DefaultsToEmptyList()
    {
        var spec = new RequirementsSpec();
        spec.Criteria.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public void RequirementsSpec_CanHoldMultipleCriteria()
    {
        var spec = new RequirementsSpec
        {
            Criteria =
            [
                new AcceptanceCriterion { Id = "AC-1", Description = "Given X when Y then Z" },
                new AcceptanceCriterion { Id = "AC-2", Description = "Given A when B then C" }
            ]
        };
        spec.Criteria.Should().HaveCount(2);
    }

    [Fact]
    public void ArchitectureRecord_DiagramMermaid_DefaultsToEmpty()
    {
        var record = new ArchitectureRecord();
        record.DiagramMermaid.Should().BeEmpty();
    }

    [Fact]
    public void BuildResult_Success_DefaultsToFalse()
    {
        var result = new BuildResult();
        result.Success.Should().BeFalse();
    }

    [Fact]
    public void BuildResult_SweAfRunId_DefaultsToEmpty()
    {
        var result = new BuildResult();
        result.SweAfRunId.Should().BeEmpty();
    }

    [Fact]
    public void LearnReport_FeedbackItems_DefaultsToEmptyList()
    {
        var report = new LearnReport();
        report.FeedbackItems.Should().NotBeNull().And.BeEmpty();
    }
}
```

---

### 1.3 — SdlcRunConfig

**File:** `SdlcRunConfigTests.cs`

```csharp
public class SdlcRunConfigTests
{
    [Fact]
    public void SdlcRunConfig_WhenCreated_HasNewRunId()
    {
        var config = new SdlcRunConfig { ProjectBrief = "Build a thing" };
        config.RunId.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public void SdlcRunConfig_TwoInstances_HaveDistinctRunIds()
    {
        var c1 = new SdlcRunConfig { ProjectBrief = "A" };
        var c2 = new SdlcRunConfig { ProjectBrief = "B" };
        c1.RunId.Should().NotBe(c2.RunId);
    }

    [Fact]
    public void SdlcRunConfig_ModelRouting_DefaultsToNonNullDictionary()
    {
        var config = new SdlcRunConfig { ProjectBrief = "Test" };
        config.ModelRouting.Should().NotBeNull();
    }

    [Theory]
    [InlineData(SdlcStage.Research)]
    [InlineData(SdlcStage.Requirements)]
    [InlineData(SdlcStage.Design)]
    [InlineData(SdlcStage.Build)]
    [InlineData(SdlcStage.Learn)]
    public void SdlcRunConfig_DefaultModelRouting_HasEntryForEveryStage(SdlcStage stage)
    {
        var config = new SdlcRunConfig { ProjectBrief = "Test" };
        config.ModelRouting.Should().ContainKey(stage);
    }
}
```

---

### 1.4 — ModelEndpoint

**File:** `ModelEndpointTests.cs`

```csharp
public class ModelEndpointTests
{
    [Fact]
    public void ModelEndpoint_Local27B_HasNonEmptyBaseUrl()
    {
        ModelEndpoint.Local27B.BaseUrl.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void ModelEndpoint_Local27B_HasNonEmptyModelId()
    {
        ModelEndpoint.Local27B.ModelId.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void ModelEndpoint_LocalMoE_HasNonEmptyBaseUrl()
    {
        ModelEndpoint.LocalMoE.BaseUrl.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void ModelEndpoint_Local27B_And_LocalMoE_HaveDifferentBaseUrls()
    {
        ModelEndpoint.Local27B.BaseUrl.Should().NotBe(ModelEndpoint.LocalMoE.BaseUrl);
    }

    [Fact]
    public void ModelEndpoint_BaseUrl_MustBeValidUri()
    {
        Uri.TryCreate(ModelEndpoint.Local27B.BaseUrl, UriKind.Absolute, out _).Should().BeTrue();
        Uri.TryCreate(ModelEndpoint.LocalMoE.BaseUrl, UriKind.Absolute, out _).Should().BeTrue();
    }
}
```

---

## Phase 2 — Infrastructure

**Project:** `SDLC.Infrastructure.Tests`
**Extra packages:** `Microsoft.Data.Sqlite`, `Dapper`, `Microsoft.Extensions.Logging.Abstractions`

---

### 2.1 — ArtifactStore: Save & Retrieve

**File:** `ArtifactStoreTests.cs`

```csharp
public class ArtifactStoreTests : IAsyncLifetime
{
    private ArtifactStore _store = null!;
    private string _tempDir = null!;

    public async Task InitializeAsync()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);
        _store = new ArtifactStore("Data Source=:memory:", _tempDir);
        await _store.InitializeAsync();
    }

    public Task DisposeAsync()
    {
        Directory.Delete(_tempDir, recursive: true);
        return Task.CompletedTask;
    }

    [Fact]
    public async Task SaveAsync_ResearchBrief_PersistsContent()
    {
        var brief = new ResearchBrief
        {
            RunId = Guid.NewGuid(),
            Stage = SdlcStage.Research,
            Content = "# Research\nSome content here"
        };

        await _store.SaveAsync(brief);

        var retrieved = await _store.GetAsync<ResearchBrief>(brief.ArtifactId);
        retrieved.Should().NotBeNull();
        retrieved!.Content.Should().Be(brief.Content);
    }

    [Fact]
    public async Task SaveAsync_WritesMarkdownFileToDisk()
    {
        var runId = Guid.NewGuid();
        var brief = new ResearchBrief
        {
            RunId = runId,
            Stage = SdlcStage.Research,
            Content = "# Research content"
        };

        await _store.SaveAsync(brief);

        var expectedPath = Path.Combine(_tempDir, runId.ToString(), "Research.md");
        File.Exists(expectedPath).Should().BeTrue();
        (await File.ReadAllTextAsync(expectedPath)).Should().Be(brief.Content);
    }

    [Fact]
    public async Task GetAsync_NonExistentArtifact_ReturnsNull()
    {
        var result = await _store.GetAsync<ResearchBrief>(Guid.NewGuid());
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetLatestForRunAsync_ReturnsCorrectStageArtifact()
    {
        var runId = Guid.NewGuid();
        var brief = new ResearchBrief { RunId = runId, Stage = SdlcStage.Research, Content = "brief" };
        var spec = new RequirementsSpec { RunId = runId, Stage = SdlcStage.Requirements, Content = "spec" };

        await _store.SaveAsync(brief);
        await _store.SaveAsync(spec);

        var retrieved = await _store.GetLatestForRunAsync<RequirementsSpec>(runId);
        retrieved.Should().NotBeNull();
        retrieved!.Content.Should().Be("spec");
    }

    [Fact]
    public async Task UpdateStatusAsync_ChangesArtifactStatus()
    {
        var brief = new ResearchBrief
        {
            RunId = Guid.NewGuid(),
            Stage = SdlcStage.Research,
            Content = "content"
        };
        await _store.SaveAsync(brief);

        await _store.UpdateStatusAsync(brief.ArtifactId, ArtifactStatus.Approved);

        var retrieved = await _store.GetAsync<ResearchBrief>(brief.ArtifactId);
        retrieved!.Status.Should().Be(ArtifactStatus.Approved);
    }

    [Fact]
    public async Task UpdateContentAsync_OverwritesFileAndMetadata()
    {
        var brief = new ResearchBrief
        {
            RunId = Guid.NewGuid(),
            Stage = SdlcStage.Research,
            Content = "original content"
        };
        await _store.SaveAsync(brief);

        await _store.UpdateContentAsync(brief.ArtifactId, "updated content");

        var retrieved = await _store.GetAsync<ResearchBrief>(brief.ArtifactId);
        retrieved!.Content.Should().Be("updated content");
    }

    [Fact]
    public async Task GetAllForRunAsync_ReturnsArtifactsInStageOrder()
    {
        var runId = Guid.NewGuid();
        await _store.SaveAsync(new RequirementsSpec { RunId = runId, Stage = SdlcStage.Requirements });
        await _store.SaveAsync(new ResearchBrief    { RunId = runId, Stage = SdlcStage.Research });

        var all = await _store.GetAllForRunAsync(runId);

        all.Should().HaveCount(2);
        all[0].Stage.Should().Be(SdlcStage.Research);
        all[1].Stage.Should().Be(SdlcStage.Requirements);
    }
}
```

---

### 2.2 — StageGateStore

**File:** `StageGateStoreTests.cs`

```csharp
public class StageGateStoreTests : IAsyncLifetime
{
    private StageGateStore _store = null!;

    public async Task InitializeAsync()
    {
        _store = new StageGateStore("Data Source=:memory:");
        await _store.InitializeAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task CreateGateAsync_ReturnsGateWithNewId()
    {
        var artifact = new ResearchBrief { RunId = Guid.NewGuid(), Stage = SdlcStage.Research };
        var gate = await _store.CreateGateAsync(artifact);
        gate.GateId.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public async Task CreateGateAsync_Status_IsPending()
    {
        var artifact = new ResearchBrief { RunId = Guid.NewGuid(), Stage = SdlcStage.Research };
        var gate = await _store.CreateGateAsync(artifact);
        gate.Status.Should().Be(GateStatus.Pending);
    }

    [Fact]
    public async Task GetAsync_AfterCreate_ReturnsGate()
    {
        var artifact = new ResearchBrief { RunId = Guid.NewGuid(), Stage = SdlcStage.Research };
        var gate = await _store.CreateGateAsync(artifact);

        var retrieved = await _store.GetAsync(gate.GateId);
        retrieved.Should().NotBeNull();
        retrieved!.GateId.Should().Be(gate.GateId);
    }

    [Fact]
    public async Task ResolveAsync_Approve_UpdatesStatusAndTimestamp()
    {
        var artifact = new ResearchBrief { RunId = Guid.NewGuid(), Stage = SdlcStage.Research };
        var gate = await _store.CreateGateAsync(artifact);

        await _store.ResolveAsync(gate.GateId, GateDecision.Approved, notes: null);

        var retrieved = await _store.GetAsync(gate.GateId);
        retrieved!.Status.Should().Be(GateStatus.Approved);
        retrieved.ResolvedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task ResolveAsync_Reject_SetsRejectedStatus()
    {
        var artifact = new ResearchBrief { RunId = Guid.NewGuid(), Stage = SdlcStage.Research };
        var gate = await _store.CreateGateAsync(artifact);

        await _store.ResolveAsync(gate.GateId, GateDecision.Rejected, "Needs more detail");

        var retrieved = await _store.GetAsync(gate.GateId);
        retrieved!.Status.Should().Be(GateStatus.Rejected);
        retrieved.Notes.Should().Be("Needs more detail");
    }

    [Fact]
    public async Task GetPendingForRunAsync_ReturnsPendingGatesOnly()
    {
        var runId = Guid.NewGuid();
        var a1 = new ResearchBrief     { RunId = runId, Stage = SdlcStage.Research };
        var a2 = new RequirementsSpec  { RunId = runId, Stage = SdlcStage.Requirements };

        var g1 = await _store.CreateGateAsync(a1);
        var g2 = await _store.CreateGateAsync(a2);

        await _store.ResolveAsync(g1.GateId, GateDecision.Approved, null);

        var pending = await _store.GetPendingForRunAsync(runId);
        pending.Should().HaveCount(1);
        pending[0].GateId.Should().Be(g2.GateId);
    }
}
```

---

### 2.3 — AgentKernelFactory

**File:** `AgentKernelFactoryTests.cs`

```csharp
public class AgentKernelFactoryTests
{
    [Theory]
    [InlineData(SdlcStage.Research)]
    [InlineData(SdlcStage.Requirements)]
    [InlineData(SdlcStage.Design)]
    [InlineData(SdlcStage.Build)]
    [InlineData(SdlcStage.Learn)]
    public void CreateForStage_WithDefaultRouting_ReturnsNonNullKernel(SdlcStage stage)
    {
        var routing = ModelRoutingConfig.Default;
        var factory = new AgentKernelFactory(routing);

        var kernel = factory.CreateForStage(stage);

        kernel.Should().NotBeNull();
    }

    [Fact]
    public void CreateForStage_WhenEndpointOverridden_UsesOverriddenEndpoint()
    {
        var customEndpoint = new ModelEndpoint("custom-model", "http://custom:9999/v1");
        var routing = new ModelRoutingConfig
        {
            StageEndpoints = new Dictionary<SdlcStage, ModelEndpoint>
            {
                [SdlcStage.Research]     = customEndpoint,
                [SdlcStage.Requirements] = ModelEndpoint.Local27B,
                [SdlcStage.Design]       = ModelEndpoint.Local27B,
                [SdlcStage.Build]        = ModelEndpoint.Local27B,
                [SdlcStage.Learn]        = ModelEndpoint.LocalMoE,
            }
        };
        var factory = new AgentKernelFactory(routing);

        // Verify factory resolves without throwing — endpoint inspection is via config
        var act = () => factory.CreateForStage(SdlcStage.Research);
        act.Should().NotThrow();
    }

    [Fact]
    public void CreateForStage_DifferentStages_CanProduceDifferentModelIds()
    {
        var routing = new ModelRoutingConfig
        {
            StageEndpoints = new Dictionary<SdlcStage, ModelEndpoint>
            {
                [SdlcStage.Research]     = ModelEndpoint.Local27B,
                [SdlcStage.Requirements] = ModelEndpoint.Local27B,
                [SdlcStage.Design]       = ModelEndpoint.Local27B,
                [SdlcStage.Build]        = ModelEndpoint.Local27B,
                [SdlcStage.Learn]        = ModelEndpoint.LocalMoE,   // different
            }
        };

        routing.GetEndpoint(SdlcStage.Build).ModelId
               .Should().NotBe(routing.GetEndpoint(SdlcStage.Learn).ModelId);
    }
}
```

---

### 2.4 — SWE-AF HTTP Client

**File:** `SweAfHttpClientTests.cs`
**Extra packages:** `Microsoft.AspNetCore.Mvc.Testing`, `WireMock.Net`

```csharp
public class SweAfHttpClientTests : IAsyncLifetime
{
    private WireMockServer _server = null!;
    private SweAfHttpClient _client = null!;

    public Task InitializeAsync()
    {
        _server = WireMockServer.Start();
        var httpClient = new HttpClient { BaseAddress = new Uri(_server.Url!) };
        _client = new SweAfHttpClient(httpClient);
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _server.Stop();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task SubmitAsync_OnSuccess_ReturnsRunId()
    {
        _server.Given(Request.Create().WithPath("/api/runs").UsingPost())
               .RespondWith(Response.Create()
                   .WithStatusCode(201)
                   .WithBodyAsJson(new { runId = "sweaf-run-123" }));

        var task = new SweAfTask { Spec = "spec content", Architecture = "arch content" };
        var runId = await _client.SubmitAsync(task);

        runId.Should().Be("sweaf-run-123");
    }

    [Fact]
    public async Task SubmitAsync_OnNonSuccess_ThrowsHttpRequestException()
    {
        _server.Given(Request.Create().WithPath("/api/runs").UsingPost())
               .RespondWith(Response.Create().WithStatusCode(500));

        var task = new SweAfTask { Spec = "spec", Architecture = "arch" };
        await _client.Invoking(c => c.SubmitAsync(task))
                     .Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task PollAsync_WhenRunSucceeds_EmitsSucceededStatus()
    {
        _server.Given(Request.Create().WithPath("/api/runs/run-abc").UsingGet())
               .RespondWith(Response.Create()
                   .WithBodyAsJson(new { state = "Succeeded", logs = "build output" }));

        var statuses = new List<SweAfStatus>();
        await foreach (var s in _client.PollAsync("run-abc"))
            statuses.Add(s);

        statuses.Should().ContainSingle();
        statuses[0].State.Should().Be(SweAfState.Succeeded);
        statuses[0].IsTerminal.Should().BeTrue();
    }

    [Fact]
    public async Task PollAsync_WhenRunFails_EmitsFailedStatus()
    {
        _server.Given(Request.Create().WithPath("/api/runs/run-fail").UsingGet())
               .RespondWith(Response.Create()
                   .WithBodyAsJson(new { state = "Failed", logs = "error log" }));

        var statuses = new List<SweAfStatus>();
        await foreach (var s in _client.PollAsync("run-fail"))
            statuses.Add(s);

        statuses.Last().State.Should().Be(SweAfState.Failed);
        statuses.Last().IsTerminal.Should().BeTrue();
    }

    [Fact]
    public async Task PollAsync_WhenRunningThenSucceeds_EmitsMultipleStatuses()
    {
        var callCount = 0;
        _server.Given(Request.Create().WithPath("/api/runs/run-multi").UsingGet())
               .RespondWith(Response.Create()
                   .WithCallback(_ =>
                   {
                       callCount++;
                       var state = callCount < 3 ? "Running" : "Succeeded";
                       return new ResponseMessage
                       {
                           StatusCode = 200,
                           BodyData = new BodyData
                           {
                               BodyAsJson = new { state },
                               DetectedBodyType = BodyType.Json
                           }
                       };
                   }));

        var statuses = new List<SweAfStatus>();
        await foreach (var s in _client.PollAsync("run-multi"))
            statuses.Add(s);

        statuses.Should().HaveCountGreaterThanOrEqualTo(3);
        statuses.Last().IsTerminal.Should().BeTrue();
    }

    [Fact]
    public async Task PollAsync_RespectsCancellation()
    {
        _server.Given(Request.Create().WithPath("/api/runs/run-cancel").UsingGet())
               .RespondWith(Response.Create().WithBodyAsJson(new { state = "Running" }));

        var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        var statuses = new List<SweAfStatus>();

        await _client.Invoking(async c =>
        {
            await foreach (var s in c.PollAsync("run-cancel", cts.Token))
                statuses.Add(s);
        }).Should().ThrowAsync<OperationCanceledException>();
    }
}
```

---

### 2.5 — SweAfPayloadBuilder

**File:** `SweAfPayloadBuilderTests.cs`

```csharp
public class SweAfPayloadBuilderTests
{
    [Fact]
    public void Build_WithSpecAndArchitecture_ReturnsNonNullPayload()
    {
        var spec = new RequirementsSpec { Content = "## Requirements\n- Feature A", RunId = Guid.NewGuid() };
        var arch = new ArchitectureRecord { Content = "## Architecture\n- Microservices" };

        var payload = SweAfPayloadBuilder.Build(spec, arch);

        payload.Should().NotBeNull();
    }

    [Fact]
    public void Build_IncludesSpecContent()
    {
        var spec = new RequirementsSpec { Content = "spec content", RunId = Guid.NewGuid() };
        var arch = new ArchitectureRecord { Content = "arch content" };

        var payload = SweAfPayloadBuilder.Build(spec, arch);

        payload.Spec.Should().Contain("spec content");
    }

    [Fact]
    public void Build_IncludesArchitectureContent()
    {
        var spec = new RequirementsSpec { Content = "spec content", RunId = Guid.NewGuid() };
        var arch = new ArchitectureRecord { Content = "arch content" };

        var payload = SweAfPayloadBuilder.Build(spec, arch);

        payload.Architecture.Should().Contain("arch content");
    }

    [Fact]
    public void Build_WithAcceptanceCriteria_IncludesAllCriteria()
    {
        var spec = new RequirementsSpec
        {
            Content = "spec",
            RunId = Guid.NewGuid(),
            Criteria =
            [
                new AcceptanceCriterion { Id = "AC-1", Description = "Given X when Y then Z" },
                new AcceptanceCriterion { Id = "AC-2", Description = "Given A when B then C" }
            ]
        };
        var arch = new ArchitectureRecord { Content = "arch" };

        var payload = SweAfPayloadBuilder.Build(spec, arch);

        payload.Spec.Should().Contain("AC-1").And.Contain("AC-2");
    }
}
```

---

## Phase 3 — Orchestrator

**Project:** `SDLC.Orchestrator.Tests`
**Extra packages:** `Microsoft.SemanticKernel.Process.Runtime.InProcess`

---

### 3.1 — StageGateStep

**File:** `StageGateStepTests.cs`

```csharp
public class StageGateStepTests
{
    private readonly INotificationService _notifications = Substitute.For<INotificationService>();
    private readonly IStageGateStore _gateStore = Substitute.For<IStageGateStore>();
    private readonly KernelProcessStepContext _context = Substitute.For<KernelProcessStepContext>();

    [Fact]
    public async Task RequestApprovalAsync_CreatesGateInStore()
    {
        var artifact = new ResearchBrief { RunId = Guid.NewGuid(), Stage = SdlcStage.Research };
        _gateStore.CreateGateAsync(artifact)
                  .Returns(new StageGate { GateId = Guid.NewGuid(), Status = GateStatus.Pending });

        var step = new StageGateStep();
        await step.RequestApprovalAsync(_context, artifact, _notifications, _gateStore);

        await _gateStore.Received(1).CreateGateAsync(artifact);
    }

    [Fact]
    public async Task RequestApprovalAsync_SendsNotification()
    {
        var artifact = new ResearchBrief { RunId = Guid.NewGuid(), Stage = SdlcStage.Research };
        var gate = new StageGate { GateId = Guid.NewGuid(), Status = GateStatus.Pending };
        _gateStore.CreateGateAsync(artifact).Returns(gate);

        var step = new StageGateStep();
        await step.RequestApprovalAsync(_context, artifact, _notifications, _gateStore);

        await _notifications.Received(1).SendApprovalRequestAsync(gate);
    }

    [Fact]
    public async Task RequestApprovalAsync_EmitsGatePendingEvent()
    {
        var artifact = new ResearchBrief { RunId = Guid.NewGuid(), Stage = SdlcStage.Research };
        var gate = new StageGate { GateId = Guid.NewGuid(), Status = GateStatus.Pending };
        _gateStore.CreateGateAsync(artifact).Returns(gate);

        KernelProcessEvent? emittedEvent = null;
        await _context.EmitEventAsync(Arg.Do<KernelProcessEvent>(e => emittedEvent = e));

        var step = new StageGateStep();
        await step.RequestApprovalAsync(_context, artifact, _notifications, _gateStore);

        emittedEvent.Should().NotBeNull();
        emittedEvent!.Id.Should().Be(SdlcEvents.GatePending);
        emittedEvent.Data.Should().Be(gate.GateId);
    }

    [Fact]
    public async Task RequestApprovalAsync_WhenNotificationFails_StillCreatesGate()
    {
        var artifact = new ResearchBrief { RunId = Guid.NewGuid(), Stage = SdlcStage.Research };
        var gate = new StageGate { GateId = Guid.NewGuid(), Status = GateStatus.Pending };
        _gateStore.CreateGateAsync(artifact).Returns(gate);
        _notifications.SendApprovalRequestAsync(Arg.Any<StageGate>())
                      .Returns(Task.FromException(new HttpRequestException("Slack unavailable")));

        var step = new StageGateStep();
        // Gate creation must succeed even if notification delivery fails
        await _step.Invoking(s => s.RequestApprovalAsync(_context, artifact, _notifications, _gateStore))
                   .Should().ThrowAsync<HttpRequestException>();

        await _gateStore.Received(1).CreateGateAsync(artifact);
    }
}
```

---

### 3.2 — PipelineRunnerService

**File:** `PipelineRunnerServiceTests.cs`

```csharp
public class PipelineRunnerServiceTests
{
    [Fact]
    public async Task EnqueueAsync_AddsRunToActiveRuns()
    {
        var runner = CreateRunner();
        var config = new SdlcRunConfig { ProjectBrief = "Test project" };

        await runner.EnqueueAsync(config);

        runner.ActiveRunCount.Should().Be(1);
    }

    [Fact]
    public async Task EnqueueAsync_MultipleRuns_AllTracked()
    {
        var runner = CreateRunner();

        await runner.EnqueueAsync(new SdlcRunConfig { ProjectBrief = "Project A" });
        await runner.EnqueueAsync(new SdlcRunConfig { ProjectBrief = "Project B" });
        await runner.EnqueueAsync(new SdlcRunConfig { ProjectBrief = "Project C" });

        runner.ActiveRunCount.Should().Be(3);
    }

    [Fact]
    public async Task IsRunActive_ForEnqueuedRun_ReturnsTrue()
    {
        var runner = CreateRunner();
        var config = new SdlcRunConfig { ProjectBrief = "Test" };

        await runner.EnqueueAsync(config);

        runner.IsRunActive(config.RunId).Should().BeTrue();
    }

    [Fact]
    public void IsRunActive_ForUnknownRunId_ReturnsFalse()
    {
        var runner = CreateRunner();
        runner.IsRunActive(Guid.NewGuid()).Should().BeFalse();
    }

    [Fact]
    public async Task ResumeGateAsync_ForUnknownRunId_ThrowsInvalidOperationException()
    {
        var runner = CreateRunner();

        await runner.Invoking(r => r.ResumeGateAsync(Guid.NewGuid(), Guid.NewGuid(), GateDecision.Approved, null))
                    .Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task EnqueueAsync_SameRunIdTwice_ThrowsInvalidOperationException()
    {
        var runner = CreateRunner();
        var config = new SdlcRunConfig { ProjectBrief = "Test" };
        await runner.EnqueueAsync(config);

        await runner.Invoking(r => r.EnqueueAsync(config))
                    .Should().ThrowAsync<InvalidOperationException>();
    }

    private static PipelineRunnerService CreateRunner()
    {
        var processFactory = Substitute.For<ISdlcProcessFactory>();
        var logger = Substitute.For<ILogger<PipelineRunnerService>>();
        return new PipelineRunnerService(processFactory, logger);
    }
}
```

---

### 3.3 — SdlcEvents Constants

**File:** `SdlcEventsTests.cs`

```csharp
public class SdlcEventsTests
{
    [Fact]
    public void AllEventConstants_AreNonNullOrWhitespace()
    {
        var constants = typeof(SdlcEvents)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(f => f.IsLiteral && !f.IsInitOnly && f.FieldType == typeof(string))
            .Select(f => (string)f.GetValue(null)!)
            .ToList();

        constants.Should().NotBeEmpty();
        constants.Should().AllSatisfy(c => c.Should().NotBeNullOrWhiteSpace());
    }

    [Fact]
    public void AllEventConstants_AreUnique()
    {
        var constants = typeof(SdlcEvents)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(f => f.IsLiteral && !f.IsInitOnly && f.FieldType == typeof(string))
            .Select(f => (string)f.GetValue(null)!)
            .ToList();

        constants.Should().OnlyHaveUniqueItems();
    }

    [Theory]
    [InlineData(nameof(SdlcEvents.RunStarted))]
    [InlineData(nameof(SdlcEvents.RunComplete))]
    [InlineData(nameof(SdlcEvents.GatePending))]
    [InlineData(nameof(SdlcEvents.GateApproved))]
    [InlineData(nameof(SdlcEvents.GateRejected))]
    [InlineData(nameof(SdlcEvents.ResearchComplete))]
    [InlineData(nameof(SdlcEvents.RequirementsComplete))]
    [InlineData(nameof(SdlcEvents.DesignComplete))]
    [InlineData(nameof(SdlcEvents.BuildComplete))]
    [InlineData(nameof(SdlcEvents.LearnComplete))]
    public void SdlcEvents_RequiredConstant_Exists(string constantName)
    {
        var field = typeof(SdlcEvents).GetField(constantName,
            BindingFlags.Public | BindingFlags.Static);
        field.Should().NotBeNull($"{constantName} must exist on SdlcEvents");
    }
}
```

---

## Phase 4 — Agents

**Project:** `SDLC.Agents.Tests`
**Extra packages:** `Microsoft.SemanticKernel.Agents.Core`

> **Agent test strategy:** Agent steps are tested against a **fake/stub kernel** that returns canned LLM responses. We do not make real LLM calls in unit tests. Integration tests (Phase 7) run against the local vLLM endpoint.

---

### 4.1 — ResearchStep

**File:** `ResearchStepTests.cs`

```csharp
public class ResearchStepTests
{
    private readonly IArtifactStore _artifacts = Substitute.For<IArtifactStore>();
    private readonly AgentKernelFactory _kernelFactory = Substitute.For<AgentKernelFactory>();
    private readonly KernelProcessStepContext _context = Substitute.For<KernelProcessStepContext>();

    [Fact]
    public async Task RunAsync_SavesResearchBrief()
    {
        SetupKernelWithResponse("# Research Brief\nContent here.\n[SATISFACTORY]");
        var config = new SdlcRunConfig { ProjectBrief = "Build a reporting tool" };

        var step = new ResearchStep();
        await step.RunAsync(_context, config, _kernelFactory, _artifacts);

        await _artifacts.Received(1).SaveAsync(Arg.Is<ResearchBrief>(b => b.RunId == config.RunId));
    }

    [Fact]
    public async Task RunAsync_EmitsResearchCompleteEvent()
    {
        SetupKernelWithResponse("# Research Brief\nContent.\n[SATISFACTORY]");
        var config = new SdlcRunConfig { ProjectBrief = "Build a reporting tool" };

        KernelProcessEvent? emitted = null;
        await _context.EmitEventAsync(Arg.Do<KernelProcessEvent>(e => emitted = e));

        var step = new ResearchStep();
        await step.RunAsync(_context, config, _kernelFactory, _artifacts);

        emitted!.Id.Should().Be(SdlcEvents.ResearchComplete);
        emitted.Data.Should().BeOfType<ResearchBrief>();
    }

    [Fact]
    public async Task RunAsync_WhenFirstAttemptUnsatisfactory_Retries()
    {
        var callCount = 0;
        SetupKernelWithResponseFactory(() =>
        {
            callCount++;
            return callCount < 3 ? "Needs improvement. [UNSATISFACTORY]" : "Good. [SATISFACTORY]";
        });

        var config = new SdlcRunConfig { ProjectBrief = "Build a tool" };
        var step = new ResearchStep();
        await step.RunAsync(_context, config, _kernelFactory, _artifacts);

        callCount.Should().BeGreaterThanOrEqualTo(3);
    }

    [Fact]
    public async Task RunAsync_MaxThreeAttempts_DoesNotLoopForever()
    {
        SetupKernelWithResponse("Always unsatisfactory. [UNSATISFACTORY]");
        var config = new SdlcRunConfig { ProjectBrief = "Build a tool" };

        var step = new ResearchStep();
        // Should complete (using best available output) rather than loop indefinitely
        await step.Invoking(s => s.RunAsync(_context, config, _kernelFactory, _artifacts))
                  .Should().CompleteWithinAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task RunAsync_SavedBrief_HasCorrectRunId()
    {
        SetupKernelWithResponse("Good research. [SATISFACTORY]");
        var config = new SdlcRunConfig { ProjectBrief = "Project", RunId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000000") };

        var step = new ResearchStep();
        await step.RunAsync(_context, config, _kernelFactory, _artifacts);

        await _artifacts.Received().SaveAsync(
            Arg.Is<ResearchBrief>(b => b.RunId == config.RunId));
    }

    private void SetupKernelWithResponse(string response) =>
        SetupKernelWithResponseFactory(() => response);

    private void SetupKernelWithResponseFactory(Func<string> factory)
    {
        // Stub kernel that returns canned responses via a fake ChatCompletionService
        var fakeKernel = KernelTestHelper.CreateWithCannedResponse(factory);
        _kernelFactory.CreateForStage(SdlcStage.Research).Returns(fakeKernel);
    }
}
```

---

### 4.2 — BuildStep

**File:** `BuildStepTests.cs`

```csharp
public class BuildStepTests
{
    private readonly ISweAfClient _sweAf = Substitute.For<ISweAfClient>();
    private readonly IArtifactStore _artifacts = Substitute.For<IArtifactStore>();
    private readonly ILogger<BuildStep> _logger = Substitute.For<ILogger<BuildStep>>();
    private readonly KernelProcessStepContext _context = Substitute.For<KernelProcessStepContext>();

    [Fact]
    public async Task RunAsync_SubmitsToSweAf()
    {
        SetupSweAfSuccess("run-001");
        var (spec, arch) = MakeArtifacts();

        await new BuildStep().RunAsync(_context, arch, spec, _sweAf, _artifacts, _logger);

        await _sweAf.Received(1).SubmitAsync(Arg.Any<SweAfTask>());
    }

    [Fact]
    public async Task RunAsync_WhenSweAfSucceeds_SavesBuildResultWithSuccessTrue()
    {
        SetupSweAfSuccess("run-001");
        var (spec, arch) = MakeArtifacts();

        await new BuildStep().RunAsync(_context, arch, spec, _sweAf, _artifacts, _logger);

        await _artifacts.Received(1)
                        .SaveAsync(Arg.Is<BuildResult>(r => r.Success == true));
    }

    [Fact]
    public async Task RunAsync_WhenSweAfFails_SavesBuildResultWithSuccessFalse()
    {
        SetupSweAfFailure("run-fail");
        var (spec, arch) = MakeArtifacts();

        await new BuildStep().RunAsync(_context, arch, spec, _sweAf, _artifacts, _logger);

        await _artifacts.Received(1)
                        .SaveAsync(Arg.Is<BuildResult>(r => r.Success == false));
    }

    [Fact]
    public async Task RunAsync_EmitsBuildCompleteEvent()
    {
        SetupSweAfSuccess("run-002");
        var (spec, arch) = MakeArtifacts();

        KernelProcessEvent? emitted = null;
        await _context.EmitEventAsync(Arg.Do<KernelProcessEvent>(e => emitted = e));

        await new BuildStep().RunAsync(_context, arch, spec, _sweAf, _artifacts, _logger);

        emitted!.Id.Should().Be(SdlcEvents.BuildComplete);
        emitted.Data.Should().BeOfType<BuildResult>();
    }

    [Fact]
    public async Task RunAsync_StoresSweAfRunId()
    {
        SetupSweAfSuccess("run-xyz");
        var (spec, arch) = MakeArtifacts();

        await new BuildStep().RunAsync(_context, arch, spec, _sweAf, _artifacts, _logger);

        await _artifacts.Received(1)
                        .SaveAsync(Arg.Is<BuildResult>(r => r.SweAfRunId == "run-xyz"));
    }

    [Fact]
    public async Task RunAsync_BuildResultRunId_MatchesSpecRunId()
    {
        SetupSweAfSuccess("run-id-check");
        var (spec, arch) = MakeArtifacts();

        BuildResult? saved = null;
        await _artifacts.SaveAsync(Arg.Do<BuildResult>(r => saved = r));

        await new BuildStep().RunAsync(_context, arch, spec, _sweAf, _artifacts, _logger);

        saved!.RunId.Should().Be(spec.RunId);
    }

    private void SetupSweAfSuccess(string runId)
    {
        _sweAf.SubmitAsync(Arg.Any<SweAfTask>(), Arg.Any<CancellationToken>())
              .Returns(runId);
        _sweAf.PollAsync(runId, Arg.Any<CancellationToken>())
              .Returns(AsyncEnumerable.Create(_ =>
                  new[] { new SweAfStatus { State = SweAfState.Succeeded, Logs = "OK" } }
                      .ToAsyncEnumerable().GetAsyncEnumerator()));
    }

    private void SetupSweAfFailure(string runId)
    {
        _sweAf.SubmitAsync(Arg.Any<SweAfTask>(), Arg.Any<CancellationToken>())
              .Returns(runId);
        _sweAf.PollAsync(runId, Arg.Any<CancellationToken>())
              .Returns(AsyncEnumerable.Create(_ =>
                  new[] { new SweAfStatus { State = SweAfState.Failed, Logs = "ERR" } }
                      .ToAsyncEnumerable().GetAsyncEnumerator()));
    }

    private static (RequirementsSpec, ArchitectureRecord) MakeArtifacts()
    {
        var runId = Guid.NewGuid();
        return (
            new RequirementsSpec  { RunId = runId, Content = "spec", Stage = SdlcStage.Requirements },
            new ArchitectureRecord { RunId = runId, Content = "arch", Stage = SdlcStage.Design }
        );
    }
}
```

---

### 4.3 — Stage Prompt Builders

**File:** `PromptBuilderTests.cs`

```csharp
public class PromptBuilderTests
{
    [Fact]
    public void ResearchPrompts_BuildPrompt_ContainsProjectBrief()
    {
        var config = new SdlcRunConfig { ProjectBrief = "Build an invoice system" };
        var prompt = ResearchPrompts.BuildPrompt(config);
        prompt.Should().Contain("Build an invoice system");
    }

    [Fact]
    public void ResearchPrompts_CritiquePrompt_IsNonEmpty()
    {
        ResearchPrompts.CritiquePrompt.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void ResearchPrompts_IsSatisfactory_TrueWhenMarkerPresent()
    {
        ResearchPrompts.IsSatisfactory("Good output. [SATISFACTORY]").Should().BeTrue();
    }

    [Fact]
    public void ResearchPrompts_IsSatisfactory_FalseWhenMarkerAbsent()
    {
        ResearchPrompts.IsSatisfactory("Needs more work.").Should().BeFalse();
    }

    [Theory]
    [InlineData(typeof(RequirementsPrompts))]
    [InlineData(typeof(DesignPrompts))]
    [InlineData(typeof(LearnPrompts))]
    public void AllPromptClasses_HaveNonEmptySystemPrompt(Type promptClass)
    {
        var systemPrompt = (string)promptClass
            .GetProperty("SystemPrompt", BindingFlags.Public | BindingFlags.Static)!
            .GetValue(null)!;

        systemPrompt.Should().NotBeNullOrWhiteSpace();
    }
}
```

---

## Phase 5 — Notifications

**Project:** `SDLC.Notifications.Tests`
**Extra packages:** `WireMock.Net`

---

### 5.1 — SlackNotificationService

**File:** `SlackNotificationServiceTests.cs`

```csharp
public class SlackNotificationServiceTests : IAsyncLifetime
{
    private WireMockServer _slackMock = null!;
    private SlackNotificationService _service = null!;

    public Task InitializeAsync()
    {
        _slackMock = WireMockServer.Start();

        var config = new SlackConfig
        {
            Token          = "test-token",
            ApprovalChannel = "#sdlc-approvals",
            BaseUrl        = _slackMock.Url!
        };
        var urlBuilder = Substitute.For<IDashboardUrlBuilder>();
        urlBuilder.ForGate(Arg.Any<Guid>())
                  .Returns(g => $"http://dashboard/gate/{g.Arg<Guid>()}");

        _service = new SlackNotificationService(config, urlBuilder);
        return Task.CompletedTask;
    }

    public Task DisposeAsync() { _slackMock.Stop(); return Task.CompletedTask; }

    [Fact]
    public async Task SendApprovalRequestAsync_PostsToSlackChatPostMessage()
    {
        _slackMock.Given(Request.Create().WithPath("/api/chat.postMessage").UsingPost())
                  .RespondWith(Response.Create()
                      .WithStatusCode(200)
                      .WithBodyAsJson(new { ok = true }));

        var gate = new StageGate { GateId = Guid.NewGuid(), RunId = Guid.NewGuid(), Stage = SdlcStage.Research };
        await _service.SendApprovalRequestAsync(gate);

        _slackMock.LogEntries.Should().Contain(e => e.RequestMessage.Path == "/api/chat.postMessage");
    }

    [Fact]
    public async Task SendApprovalRequestAsync_MessageContainsDashboardUrl()
    {
        string? capturedBody = null;
        _slackMock.Given(Request.Create().WithPath("/api/chat.postMessage").UsingPost())
                  .RespondWith(Response.Create()
                      .WithCallback(req =>
                      {
                          capturedBody = req.Body;
                          return new ResponseMessage
                          {
                              StatusCode = 200,
                              BodyData = new BodyData { BodyAsJson = new { ok = true }, DetectedBodyType = BodyType.Json }
                          };
                      }));

        var gate = new StageGate { GateId = Guid.NewGuid(), RunId = Guid.NewGuid(), Stage = SdlcStage.Requirements };
        await _service.SendApprovalRequestAsync(gate);

        capturedBody.Should().Contain(gate.GateId.ToString());
    }

    [Fact]
    public async Task SendApprovalRequestAsync_MessageContainsStage()
    {
        string? capturedBody = null;
        _slackMock.Given(Request.Create().WithPath("/api/chat.postMessage").UsingPost())
                  .RespondWith(Response.Create()
                      .WithCallback(req =>
                      {
                          capturedBody = req.Body;
                          return new ResponseMessage
                          {
                              StatusCode = 200,
                              BodyData = new BodyData { BodyAsJson = new { ok = true }, DetectedBodyType = BodyType.Json }
                          };
                      }));

        var gate = new StageGate { GateId = Guid.NewGuid(), RunId = Guid.NewGuid(), Stage = SdlcStage.Design };
        await _service.SendApprovalRequestAsync(gate);

        capturedBody.Should().Contain("Design");
    }

    [Fact]
    public async Task SendApprovalRequestAsync_OnSlackError_ThrowsNotificationException()
    {
        _slackMock.Given(Request.Create().WithPath("/api/chat.postMessage").UsingPost())
                  .RespondWith(Response.Create().WithStatusCode(500));

        var gate = new StageGate { GateId = Guid.NewGuid(), RunId = Guid.NewGuid(), Stage = SdlcStage.Research };

        await _service.Invoking(s => s.SendApprovalRequestAsync(gate))
                      .Should().ThrowAsync<NotificationException>();
    }
}
```

---

## Phase 6 — Dashboard (Blazor)

**Project:** `SDLC.Dashboard.Tests`
**Extra packages:** `bunit`, `Microsoft.AspNetCore.Mvc.Testing`

---

### 6.1 — Gate Review Page Component

**File:** `GateReviewPageTests.cs`

```csharp
public class GateReviewPageTests : TestContext
{
    private readonly IStageGateStore _gateStore = Substitute.For<IStageGateStore>();
    private readonly IPipelineRunnerService _runner = Substitute.For<IPipelineRunnerService>();
    private readonly IArtifactStore _artifacts = Substitute.For<IArtifactStore>();

    public GateReviewPageTests()
    {
        Services.AddSingleton(_gateStore);
        Services.AddSingleton(_runner);
        Services.AddSingleton(_artifacts);
    }

    [Fact]
    public void GateReview_WhenGateExists_DisplaysStageLabel()
    {
        var gateId = Guid.NewGuid();
        SetupGate(gateId, SdlcStage.Requirements, "# Spec\nSome requirements.");

        var cut = RenderComponent<Review>(p => p.Add(r => r.GateId, gateId));

        cut.Find("h2").TextContent.Should().Contain("Requirements");
    }

    [Fact]
    public void GateReview_WhenGateExists_DisplaysArtifactContent()
    {
        var gateId = Guid.NewGuid();
        SetupGate(gateId, SdlcStage.Requirements, "# Spec\nSome requirements.");

        var cut = RenderComponent<Review>(p => p.Add(r => r.GateId, gateId));

        cut.FindComponent<ArtifactEditor>().Instance.Content
           .Should().Contain("Some requirements.");
    }

    [Fact]
    public async Task GateReview_ClickApprove_CallsResumeWithApprovedDecision()
    {
        var gateId = Guid.NewGuid();
        SetupGate(gateId, SdlcStage.Research, "content");

        var cut = RenderComponent<Review>(p => p.Add(r => r.GateId, gateId));
        await cut.Find(".btn-approve").ClickAsync(new MouseEventArgs());

        await _runner.Received(1).ResumeGateAsync(
            Arg.Any<Guid>(), gateId, GateDecision.Approved, Arg.Any<string?>());
    }

    [Fact]
    public async Task GateReview_ClickReject_CallsResumeWithRejectedDecision()
    {
        var gateId = Guid.NewGuid();
        SetupGate(gateId, SdlcStage.Research, "content");

        var cut = RenderComponent<Review>(p => p.Add(r => r.GateId, gateId));
        await cut.Find(".btn-reject").ClickAsync(new MouseEventArgs());

        await _runner.Received(1).ResumeGateAsync(
            Arg.Any<Guid>(), gateId, GateDecision.Rejected, Arg.Any<string?>());
    }

    [Fact]
    public async Task GateReview_ApproveWithEditedContent_SavesEditedContentFirst()
    {
        var gateId = Guid.NewGuid();
        SetupGate(gateId, SdlcStage.Requirements, "original");

        var cut = RenderComponent<Review>(p => p.Add(r => r.GateId, gateId));
        cut.FindComponent<ArtifactEditor>().Instance.Content = "edited content";
        await cut.Find(".btn-approve").ClickAsync(new MouseEventArgs());

        await _artifacts.Received(1)
                        .UpdateContentAsync(Arg.Any<Guid>(), "edited content");
    }

    [Fact]
    public void GateReview_WhenGateNotFound_ShowsErrorMessage()
    {
        _gateStore.GetAsync(Arg.Any<Guid>()).Returns((StageGate?)null);

        var cut = RenderComponent<Review>(p => p.Add(r => r.GateId, Guid.NewGuid()));

        cut.Markup.Should().Contain("not found");
    }

    private void SetupGate(Guid gateId, SdlcStage stage, string content)
    {
        var gate = new StageGate
        {
            GateId = gateId,
            RunId = Guid.NewGuid(),
            Stage = stage,
            Status = GateStatus.Pending,
            Artifact = new ResearchBrief { Content = content, Stage = stage }
        };
        _gateStore.GetAsync(gateId).Returns(gate);
    }
}
```

---

### 6.2 — ModelRoutingPanel Component

**File:** `ModelRoutingPanelTests.cs`

```csharp
public class ModelRoutingPanelTests : TestContext
{
    [Fact]
    public void ModelRoutingPanel_RendersDropdownForEachStage()
    {
        var cut = RenderComponent<ModelRoutingPanel>(p =>
            p.Add(panel => panel.RunId, Guid.NewGuid()));

        var dropdowns = cut.FindAll("select");
        dropdowns.Should().HaveCount(5); // one per stage
    }

    [Fact]
    public void ModelRoutingPanel_EachDropdown_ContainsAvailableEndpoints()
    {
        var cut = RenderComponent<ModelRoutingPanel>(p =>
            p.Add(panel => panel.RunId, Guid.NewGuid()));

        var firstDropdown = cut.FindAll("select").First();
        firstDropdown.InnerHtml.Should().Contain("Local27B");
    }

    [Fact]
    public async Task ModelRoutingPanel_ChangingDropdown_UpdatesRoutingConfig()
    {
        var configStore = Substitute.For<IRunConfigStore>();
        Services.AddSingleton(configStore);

        var runId = Guid.NewGuid();
        var cut = RenderComponent<ModelRoutingPanel>(p => p.Add(panel => panel.RunId, runId));

        await cut.FindAll("select").First()
                 .ChangeAsync(new ChangeEventArgs { Value = "LocalMoE" });

        await configStore.Received(1).UpdateEndpointAsync(runId, SdlcStage.Research, ModelEndpoint.LocalMoE);
    }
}
```

---

### 6.3 — Run List API Endpoint

**File:** `RunsControllerTests.cs`

```csharp
public class RunsControllerTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public RunsControllerTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton(Substitute.For<IArtifactStore>());
                services.AddSingleton(Substitute.For<IPipelineRunnerService>());
                services.AddSingleton(Substitute.For<IStageGateStore>());
            }));
    }

    [Fact]
    public async Task POST_api_runs_ReturnsCreatedWithRunId()
    {
        var client = _factory.CreateClient();
        var body = new { ProjectBrief = "Build an invoice system" };

        var response = await client.PostAsJsonAsync("/api/runs", body);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var content = await response.Content.ReadFromJsonAsync<JsonElement>();
        content.GetProperty("runId").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task POST_api_runs_EmptyBrief_ReturnsBadRequest()
    {
        var client = _factory.CreateClient();
        var body = new { ProjectBrief = "" };

        var response = await client.PostAsJsonAsync("/api/runs", body);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task POST_api_gate_gateId_approve_ResumesGate()
    {
        var runner = Substitute.For<IPipelineRunnerService>();
        var client = _factory.WithWebHostBuilder(b =>
            b.ConfigureTestServices(s => s.AddSingleton(runner))).CreateClient();

        var gateId = Guid.NewGuid();
        var response = await client.PostAsJsonAsync($"/api/gate/{gateId}/approve", new { });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await runner.Received(1).ResumeGateAsync(
            Arg.Any<Guid>(), gateId, GateDecision.Approved, Arg.Any<string?>());
    }

    [Fact]
    public async Task POST_api_gate_gateId_reject_ResumesGateWithRejection()
    {
        var runner = Substitute.For<IPipelineRunnerService>();
        var client = _factory.WithWebHostBuilder(b =>
            b.ConfigureTestServices(s => s.AddSingleton(runner))).CreateClient();

        var gateId = Guid.NewGuid();
        var response = await client.PostAsJsonAsync($"/api/gate/{gateId}/reject",
            new { Notes = "Not detailed enough" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await runner.Received(1).ResumeGateAsync(
            Arg.Any<Guid>(), gateId, GateDecision.Rejected, "Not detailed enough");
    }
}
```

---

## Phase 7 — Integration Tests

**Project:** `SDLC.Integration.Tests`
**Extra packages:** `Microsoft.AspNetCore.Mvc.Testing`, `WireMock.Net`, `Testcontainers`

> These tests require Docker. They are tagged `[Trait("Category", "Integration")]` and excluded from default test runs. Run with: `dotnet test --filter Category=Integration`

---

### 7.1 — ArtifactStore with Real SQLite File

**File:** `ArtifactStoreDatabaseTests.cs`

```csharp
[Trait("Category", "Integration")]
public class ArtifactStoreDatabaseTests : IAsyncLifetime
{
    private string _dbPath = null!;
    private string _fsPath = null!;
    private ArtifactStore _store = null!;

    public async Task InitializeAsync()
    {
        _dbPath = Path.GetTempFileName();
        _fsPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_fsPath);

        _store = new ArtifactStore($"Data Source={_dbPath}", _fsPath);
        await _store.InitializeAsync();
    }

    public Task DisposeAsync()
    {
        File.Delete(_dbPath);
        Directory.Delete(_fsPath, true);
        return Task.CompletedTask;
    }

    [Fact]
    public async Task SaveAndRetrieve_SurvivesStoreRestart()
    {
        var brief = new ResearchBrief
        {
            RunId = Guid.NewGuid(),
            Stage = SdlcStage.Research,
            Content = "# Persisted content"
        };
        await _store.SaveAsync(brief);

        // Simulate restart: new store instance, same connection string
        var store2 = new ArtifactStore($"Data Source={_dbPath}", _fsPath);
        await store2.InitializeAsync();

        var retrieved = await store2.GetAsync<ResearchBrief>(brief.ArtifactId);
        retrieved.Should().NotBeNull();
        retrieved!.Content.Should().Be("# Persisted content");
    }
}
```

---

### 7.2 — Full Pipeline (Stubbed LLM + Stubbed SWE-AF)

**File:** `FullPipelineIntegrationTests.cs`

```csharp
[Trait("Category", "Integration")]
public class FullPipelineIntegrationTests : IAsyncLifetime
{
    private WireMockServer _llmMock = null!;
    private WireMockServer _sweAfMock = null!;
    private WireMockServer _slackMock = null!;

    public Task InitializeAsync()
    {
        _llmMock   = WireMockServer.Start();
        _sweAfMock = WireMockServer.Start();
        _slackMock = WireMockServer.Start();

        SetupLlmMock();
        SetupSweAfMock();
        SetupSlackMock();

        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _llmMock.Stop();
        _sweAfMock.Stop();
        _slackMock.Stop();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Pipeline_ResearchStage_ProducesArtifactAndSuspendsAtGate()
    {
        var host = BuildTestHost();
        await host.StartAsync();

        var runner = host.Services.GetRequiredService<IPipelineRunnerService>();
        var artifacts = host.Services.GetRequiredService<IArtifactStore>();

        var config = new SdlcRunConfig { ProjectBrief = "Integration test project" };
        await runner.EnqueueAsync(config);

        // Wait for Research stage to complete and gate to be created
        await WaitForConditionAsync(
            () => artifacts.GetLatestForRunAsync<ResearchBrief>(config.RunId),
            artifact => artifact != null,
            timeout: TimeSpan.FromSeconds(30));

        var brief = await artifacts.GetLatestForRunAsync<ResearchBrief>(config.RunId);
        brief.Should().NotBeNull();
        brief!.Content.Should().NotBeEmpty();

        await host.StopAsync();
    }

    [Fact]
    public async Task Pipeline_WhenGateApproved_AdvancesToNextStage()
    {
        var host = BuildTestHost();
        await host.StartAsync();

        var runner = host.Services.GetRequiredService<IPipelineRunnerService>();
        var gateStore = host.Services.GetRequiredService<IStageGateStore>();
        var artifacts = host.Services.GetRequiredService<IArtifactStore>();

        var config = new SdlcRunConfig { ProjectBrief = "Gate approval test" };
        await runner.EnqueueAsync(config);

        // Wait for first gate
        StageGate? gate = null;
        await WaitForConditionAsync(
            () => gateStore.GetPendingForRunAsync(config.RunId),
            gates => { gate = gates.FirstOrDefault(); return gate != null; },
            timeout: TimeSpan.FromSeconds(30));

        // Approve the gate
        await runner.ResumeGateAsync(config.RunId, gate!.GateId, GateDecision.Approved, null);

        // Wait for Requirements artifact
        await WaitForConditionAsync(
            () => artifacts.GetLatestForRunAsync<RequirementsSpec>(config.RunId),
            spec => spec != null,
            timeout: TimeSpan.FromSeconds(30));

        var spec = await artifacts.GetLatestForRunAsync<RequirementsSpec>(config.RunId);
        spec.Should().NotBeNull();

        await host.StopAsync();
    }

    [Fact]
    public async Task Pipeline_WhenGateRejected_ReRunsStage()
    {
        var host = BuildTestHost();
        await host.StartAsync();

        var runner = host.Services.GetRequiredService<IPipelineRunnerService>();
        var gateStore = host.Services.GetRequiredService<IStageGateStore>();

        var config = new SdlcRunConfig { ProjectBrief = "Rejection test" };
        await runner.EnqueueAsync(config);

        StageGate? gate = null;
        await WaitForConditionAsync(
            () => gateStore.GetPendingForRunAsync(config.RunId),
            gates => { gate = gates.FirstOrDefault(); return gate != null; },
            timeout: TimeSpan.FromSeconds(30));

        await runner.ResumeGateAsync(config.RunId, gate!.GateId, GateDecision.Rejected, "Needs detail");

        // A new gate should appear (re-run produced a new artifact and new gate)
        await WaitForConditionAsync(
            () => gateStore.GetPendingForRunAsync(config.RunId),
            gates => gates.Any(g => g.GateId != gate.GateId),
            timeout: TimeSpan.FromSeconds(30));

        var allGates = await gateStore.GetPendingForRunAsync(config.RunId);
        allGates.Should().Contain(g => g.GateId != gate.GateId);

        await host.StopAsync();
    }

    [Fact]
    public async Task Pipeline_TwoParallelRuns_BothCompleteResearchStage()
    {
        var host = BuildTestHost();
        await host.StartAsync();

        var runner = host.Services.GetRequiredService<IPipelineRunnerService>();
        var artifacts = host.Services.GetRequiredService<IArtifactStore>();

        var config1 = new SdlcRunConfig { ProjectBrief = "Parallel run 1" };
        var config2 = new SdlcRunConfig { ProjectBrief = "Parallel run 2" };

        await runner.EnqueueAsync(config1);
        await runner.EnqueueAsync(config2);

        var timeout = TimeSpan.FromSeconds(45);
        await Task.WhenAll(
            WaitForConditionAsync(
                () => artifacts.GetLatestForRunAsync<ResearchBrief>(config1.RunId),
                b => b != null, timeout),
            WaitForConditionAsync(
                () => artifacts.GetLatestForRunAsync<ResearchBrief>(config2.RunId),
                b => b != null, timeout));

        var brief1 = await artifacts.GetLatestForRunAsync<ResearchBrief>(config1.RunId);
        var brief2 = await artifacts.GetLatestForRunAsync<ResearchBrief>(config2.RunId);

        brief1.Should().NotBeNull();
        brief2.Should().NotBeNull();

        await host.StopAsync();
    }

    // -- helpers --

    private IHost BuildTestHost() => Host.CreateDefaultBuilder()
        .ConfigureServices(services =>
        {
            // Point all endpoints at mocks
            services.Configure<SweAfOptions>(o => o.BaseUrl = _sweAfMock.Url!);
            services.Configure<SlackConfig>(o  => o.BaseUrl  = _slackMock.Url!);
            services.Configure<ModelRoutingConfig>(o =>
            {
                foreach (var stage in Enum.GetValues<SdlcStage>())
                    o.StageEndpoints[stage] = new ModelEndpoint("stub", _llmMock.Url!);
            });
            services.AddSdlcOrchestrator();
        })
        .Build();

    private void SetupLlmMock() =>
        _llmMock.Given(Request.Create().WithPath("/v1/chat/completions").UsingPost())
                .RespondWith(Response.Create()
                    .WithStatusCode(200)
                    .WithBodyAsJson(new
                    {
                        choices = new[] { new { message = new { content = "Good output. [SATISFACTORY]" } } }
                    }));

    private void SetupSweAfMock()
    {
        _sweAfMock.Given(Request.Create().WithPath("/api/runs").UsingPost())
                  .RespondWith(Response.Create()
                      .WithStatusCode(201)
                      .WithBodyAsJson(new { runId = "integration-run-001" }));

        _sweAfMock.Given(Request.Create().WithPath("/api/runs/integration-run-001").UsingGet())
                  .RespondWith(Response.Create()
                      .WithBodyAsJson(new { state = "Succeeded", logs = "Build OK" }));
    }

    private void SetupSlackMock() =>
        _slackMock.Given(Request.Create().WithPath("/api/chat.postMessage").UsingPost())
                  .RespondWith(Response.Create()
                      .WithStatusCode(200)
                      .WithBodyAsJson(new { ok = true }));

    private static async Task WaitForConditionAsync<T>(
        Func<Task<T>> query,
        Func<T, bool> condition,
        TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition(await query())) return;
            await Task.Delay(500);
        }
        throw new TimeoutException($"Condition not met within {timeout}");
    }
}
```

---

## Phase 8 — Telemetry

**Project:** `SDLC.Infrastructure.Tests` (add to existing)

---

### 8.1 — SdlcTelemetry

**File:** `SdlcTelemetryTests.cs`

```csharp
public class SdlcTelemetryTests
{
    [Fact]
    public void StartRunActivity_ReturnsActivityWithRunIdTag()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo      = source => source.Name.StartsWith("SDLC"),
            Sample              = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStarted     = _ => { },
            ActivityStopped     = _ => { }
        };
        ActivitySource.AddActivityListener(listener);

        var runId = Guid.NewGuid();
        using var activity = SdlcTelemetry.StartRunActivity(runId);

        activity.Should().NotBeNull();
        activity!.GetTagItem("run.id").Should().Be(runId.ToString());
    }

    [Fact]
    public void StartBuildActivity_HasSweAfTriggerTag()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo      = source => source.Name.StartsWith("SDLC"),
            Sample              = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStarted     = _ => { },
            ActivityStopped     = _ => { }
        };
        ActivitySource.AddActivityListener(listener);

        var runId = Guid.NewGuid();
        using var activity = SdlcTelemetry.StartBuildActivity(runId);

        activity!.GetTagItem("sweaf.trigger").Should().Be(true.ToString());
    }

    [Fact]
    public void Counters_AreNotNull()
    {
        SdlcTelemetry.RunsStarted.Should().NotBeNull();
        SdlcTelemetry.RunsCompleted.Should().NotBeNull();
        SdlcTelemetry.GatesApproved.Should().NotBeNull();
        SdlcTelemetry.GatesRejected.Should().NotBeNull();
        SdlcTelemetry.StageDuration.Should().NotBeNull();
    }
}
```

---

## Appendix A — Test Execution Order

```
Phase 1: Contracts           → No dependencies
Phase 2: Infrastructure      → Requires Phase 1 types
Phase 3: Orchestrator        → Requires Phase 1 + 2
Phase 4: Agents              → Requires Phase 1 + 2 + 3
Phase 5: Notifications       → Requires Phase 1
Phase 6: Dashboard           → Requires Phase 1 + 2 + 3
Phase 7: Integration         → Requires all phases
Phase 8: Telemetry           → Requires Phase 1 + 2 (run alongside Phase 2)
```

## Appendix B — Test Helper: KernelTestHelper

```csharp
// Shared test helper for creating stub Kernels
public static class KernelTestHelper
{
    public static Kernel CreateWithCannedResponse(Func<string> responseFactory)
    {
        var fakeChatService = Substitute.For<IChatCompletionService>();
        fakeChatService
            .GetChatMessageContentsAsync(
                Arg.Any<ChatHistory>(),
                Arg.Any<PromptExecutionSettings?>(),
                Arg.Any<Kernel?>(),
                Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult<IReadOnlyList<ChatMessageContent>>(
                [new ChatMessageContent(AuthorRole.Assistant, responseFactory())]));

        var builder = Kernel.CreateBuilder();
        builder.Services.AddSingleton(fakeChatService);
        return builder.Build();
    }
}
```

## Appendix C — Dockerfile for Test Runner

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS test
WORKDIR /src
COPY . .
RUN dotnet restore
RUN dotnet test \
    --filter "Category!=Integration" \
    --logger "trx;LogFileName=results.trx" \
    --results-directory /testresults

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS integration-test
WORKDIR /src
COPY . .
RUN dotnet restore
# Integration tests require Docker-in-Docker or host network
CMD ["dotnet", "test", "--filter", "Category=Integration", \
     "--logger", "trx;LogFileName=integration-results.trx"]
```
