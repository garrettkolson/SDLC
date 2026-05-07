using SDLC.Agents;
using SDLC.Contracts;
using SDLC.Dashboard.Components;
using SDLC.Notifications;
using SDLC.Orchestrator;
using SDLC.Telemetry;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddControllers();

var dbConn = builder.Configuration.GetConnectionString("SDLCDb") ?? "Data Source=sdlc.db";
var artifactDir = Path.Combine(AppContext.BaseDirectory, "artifacts");
builder.Services.AddSingleton<SDLC.Infrastructure.IArtifactStore>(
    new SDLC.Infrastructure.ArtifactStore(dbConn, artifactDir));
builder.Services.AddSingleton<SDLC.Infrastructure.IStageGateStore>(
    new SDLC.Infrastructure.StageGateStore(dbConn));

// HTTP client and notification service
builder.Services.AddHttpClient();
builder.Services.AddHttpClient<ISweAfClient>((sp, http) =>
{
    http.BaseAddress = new Uri(builder.Configuration["SweAf:BaseUrl"]
        ?? throw new InvalidOperationException("SweAf:BaseUrl required"));
    http.Timeout = TimeSpan.FromMinutes(30);
});
builder.Services.AddSingleton<SDLC.Notifications.DashboardUrlBuilder>();
builder.Services.AddSingleton<SDLC.Notifications.INotificationService>(sp =>
    new SDLC.Notifications.SlackNotificationService(
        sp.GetRequiredService<IHttpClientFactory>().CreateClient(),
        "/webhook/sdlc",
        sp.GetRequiredService<SDLC.Notifications.DashboardUrlBuilder>()));

// Telemetry
builder.Services.AddSingleton<IPipelineTelemetry, PipelineTelemetry>();

// Orchestrator
var modelRouting = builder.Configuration.GetSection("ModelRouting").Get<ModelRoutingConfig>()
    ?? ModelRoutingConfig.Default;
builder.Services.AddSingleton(modelRouting);

var dashboardBaseUrl = builder.Configuration["Dashboard:BaseUrl"]
    ?? "http://localhost:8080";
builder.Services.AddSingleton<DashboardUrlBuilder>(new DashboardUrlBuilder(dashboardBaseUrl));

builder.Services.AddSingleton<SdlcProcessFactory>();
builder.Services.AddSingleton<ISdlcProcessFactory>(sp => sp.GetRequiredService<SdlcProcessFactory>());
builder.Services.AddSingleton<PipelineRunnerService>();
builder.Services.AddSingleton<IPipelineRunner>(sp => sp.GetRequiredService<PipelineRunnerService>());
builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics => metrics.AddMeter("SDLC"));

// Simple run service — renders data, resolves gates via StageGateStore
builder.Services.AddScoped<SDLC.Dashboard.Services.ISdlcRunService>(sp =>
    new SDLC.Dashboard.Services.SdlcRunService(
        sp.GetRequiredService<SDLC.Infrastructure.IArtifactStore>(),
        sp.GetRequiredService<SDLC.Infrastructure.IStageGateStore>(),
        sp.GetRequiredService<IPipelineTelemetry>(),
        sp.GetRequiredService<IPipelineRunner>()));

var app = builder.Build();

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
