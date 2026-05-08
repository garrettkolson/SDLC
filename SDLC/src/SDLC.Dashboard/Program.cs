using OpenTelemetry;
using SDLC.Agents;
using SDLC.Contracts;
using SDLC.Dashboard.Components;
using SDLC.Dashboard.Services;
using SDLC.Infrastructure;
using SDLC.Notifications;
using SDLC.Orchestrator;
using SDLC.Telemetry;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddControllers();

var dbConn = builder.Configuration.GetConnectionString("SDLCDb")
    ?? "Data Source=sdlc.db;Pooling=True;Cache=Shared;Mode=ReadWriteCreate;";
var artifactDir = Path.Combine(AppContext.BaseDirectory, "artifacts");

builder.Services.AddSingleton<SDLC.Infrastructure.IArtifactStore>(
    new SDLC.Infrastructure.ArtifactStore(dbConn, artifactDir));
builder.Services.AddSingleton<SDLC.Infrastructure.IStageGateStore>(
    new SDLC.Infrastructure.StageGateStore(dbConn));
builder.Services.AddSingleton<SDLC.Infrastructure.RunStore>(
    new SDLC.Infrastructure.RunStore(dbConn));
builder.Services.AddSingleton<SDLC.Infrastructure.IRunStore>(sp => sp.GetRequiredService<SDLC.Infrastructure.RunStore>());

// HTTP client and notification service
builder.Services.AddHttpClient();
builder.Services.AddHttpClient<ISweAfClient>((sp, http) =>
{
    http.BaseAddress = new Uri(builder.Configuration["SweAf:BaseUrl"]
        ?? throw new InvalidOperationException("SweAf:BaseUrl required"));
    http.Timeout = TimeSpan.FromMinutes(30);
});

// Slack notification with resilience handler
var slackBaseUrl = builder.Configuration["Slack:BaseUrl"]
    ?? throw new InvalidOperationException("Slack:BaseUrl required");
builder.Services.AddHttpClient("slack")
    .AddHttpMessageHandler<SDLC.Notifications.ResilientSlackHandler>()
    .ConfigureHttpClient(h =>
    {
        h.BaseAddress = new Uri(slackBaseUrl);
        h.Timeout = TimeSpan.FromSeconds(30);
    });
builder.Services.AddSingleton<SDLC.Notifications.SlackNotificationService>();
builder.Services.AddSingleton<SDLC.Notifications.IEmailNotificationService, SDLC.Notifications.FallbackEmailNotificationService>();
builder.Services.AddSingleton<SDLC.Notifications.CompositeNotificationService>();
builder.Services.AddSingleton<SDLC.Notifications.INotificationService>(sp =>
    sp.GetRequiredService<SDLC.Notifications.CompositeNotificationService>());
builder.Services.AddHostedService<SDLC.Notifications.GateReminderService>();

// Telemetry
builder.Services.AddSingleton<IPipelineTelemetry, PipelineTelemetry>();

// Orchestrator
var modelRouting = builder.Configuration.GetSection("ModelRouting").Get<ModelRoutingConfig>()
    ?? ModelRoutingConfig.Default;
builder.Services.AddSingleton(modelRouting);

builder.Services.AddSingleton<IResilientHttpClientFactory, ResilientHttpClientFactory>();
builder.Services.AddSingleton<IKernelFactory, AgentKernelFactory>();

var dashboardBaseUrl = builder.Configuration["Dashboard:BaseUrl"]
    ?? "http://localhost:8080";
builder.Services.AddSingleton<DashboardUrlBuilder>(new DashboardUrlBuilder(dashboardBaseUrl));

builder.Services.AddSingleton<SdlcProcessFactory>();
builder.Services.AddSingleton<ISdlcProcessFactory>(sp => sp.GetRequiredService<SdlcProcessFactory>());
builder.Services.AddSingleton<PipelineRunnerService>();
builder.Services.AddSingleton<IPipelineRunner>(sp => sp.GetRequiredService<PipelineRunnerService>());
builder.Services.AddHostedService<PipelineRecoveryHostedService>();
builder.Services.AddHostedService<PipelineShutdownService>();
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddSource("SDLC.Pipeline"))
    .WithMetrics(metrics => metrics.AddMeter("SDLC"));

// VllmHealthCheck — used by /health/ready endpoint
builder.Services.AddSingleton<VllmHealthCheck>();

// Simple run service — renders data, resolves gates via StageGateStore
builder.Services.AddScoped<SDLC.Dashboard.Services.ISdlcRunService>(sp =>
    new SDLC.Dashboard.Services.SdlcRunService(
        sp.GetRequiredService<SDLC.Infrastructure.IArtifactStore>(),
        sp.GetRequiredService<SDLC.Infrastructure.IStageGateStore>(),
        sp.GetRequiredService<SDLC.Infrastructure.IRunStore>(),
        sp.GetRequiredService<IPipelineTelemetry>(),
        sp.GetRequiredService<IPipelineRunner>()));

var app = builder.Build();

// Initialize DB — tables + WAL mode
using var initScope = app.Services.CreateScope();
await initScope.ServiceProvider.GetRequiredService<IArtifactStore>().InitializeAsync();
await initScope.ServiceProvider.GetRequiredService<IStageGateStore>().InitializeAsync();
await initScope.ServiceProvider.GetRequiredService<IRunStore>().InitializeAsync();

// Health endpoints
app.MapGet("/health/live", () => Results.Ok("OK"));

app.MapGet("/health/ready", async (VllmHealthCheck vllmCheck) =>
{
    var (healthy, message) = await vllmCheck.CheckAsync();
    return healthy ? Results.Ok(message) : Results.Problem(message, statusCode: 503);
});

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapControllers();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
