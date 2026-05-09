using Azure.Identity;
using OpenTelemetry;
using Serilog;
using Serilog.Events;
using SDLC.Agents;
using SDLC.Contracts;
using SDLC.Dashboard.Components;
using SDLC.Dashboard.Services;
using SDLC.Infrastructure;
using SDLC.Infrastructure.Backup;
using SDLC.Notifications;
using SDLC.Orchestrator;
using SDLC.Telemetry;

var builder = WebApplication.CreateBuilder(args);

// Serilog — structured logging with console + OpenTelemetry sinks
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
    .MinimumLevel.Override("System", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Service", "SDLC.Dashboard")
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .WriteTo.OpenTelemetry(opts =>
    {
        opts.ResourceAttributes = new Dictionary<string, object>
        {
            ["service.name"] = "SDLC.Dashboard",
            ["service.version"] = "1.0.0"
        };
    })
    .CreateLogger();

builder.Host.UseSerilog();

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddControllers();

var dbConn = builder.Configuration.GetConnectionString("SDLCDb")
    ?? "Data Source=sdlc.db;Pooling=True;Cache=Shared;Mode=ReadWriteCreate;";
var artifactDir = Path.Combine(AppContext.BaseDirectory, "artifacts");

var dbFactory = new SDLC.Infrastructure.SqlDbConnectionFactory(dbConn);
builder.Services.AddSingleton<SDLC.Infrastructure.IDbConnectionFactory>(dbFactory);
builder.Services.AddSingleton<SDLC.Infrastructure.IArtifactStore>(
    new SDLC.Infrastructure.ArtifactStore(dbFactory, artifactDir));
builder.Services.AddSingleton<SDLC.Infrastructure.IStageGateStore>(
    new SDLC.Infrastructure.StageGateStore(dbFactory));
builder.Services.AddSingleton<SDLC.Infrastructure.RunStore>(
    new SDLC.Infrastructure.RunStore(dbFactory));
builder.Services.AddSingleton<SDLC.Infrastructure.IRunStore>(sp => sp.GetRequiredService<SDLC.Infrastructure.RunStore>());
builder.Services.AddSingleton<SDLC.Infrastructure.MigrationRunner>();

var tokenBudget = (long)(builder.Configuration.GetValue<int?>("Sdlc:TokenBudget:MaxTokensPerRun") ?? 500_000);
builder.Services.AddSingleton<Func<IRunBudgetTracker>>(sp => () => new RunBudgetTracker(tokenBudget));

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

// Backup service
var backupsDir = Path.Combine(AppContext.BaseDirectory, "backups");
builder.Services.Configure<BackupConfig>(cfg =>
{
    cfg.BackupsDirectory = backupsDir;
    cfg.DatabaseFile = "sdlc.db";
    cfg.ArtifactsDirectory = "artifacts";
    cfg.RetentionDays = 30;
    cfg.EnableAutoCleanup = true;
});
builder.Services.AddSingleton<SQLiteBackupService>();
builder.Services.AddSingleton<IFileManager, FileSystemService>();
builder.Services.AddHostedService<ScheduledBackupService>();
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

// Key Vault integration for non-dev environments
if (!builder.Environment.IsDevelopment())
{
    var vaultUri = builder.Configuration["KeyVault:Uri"];
    if (!string.IsNullOrEmpty(vaultUri))
    {
        builder.Configuration.AddAzureKeyVault(
            new Uri(vaultUri),
            new DefaultAzureCredential());
    }
}

var app = builder.Build();

// Startup validation — fail fast if required secrets are missing or placeholder (non-dev only)
if (!app.Environment.IsDevelopment())
{
    // Docker secrets — resolve _FILE suffix references (Docker/K8s convention)
    string ResolveSecret(string key)
    {
        var val = app.Configuration[key];
        if (string.IsNullOrEmpty(val)) return val;
        var fileKey = string.Join("__", key.Split(':')) + "__FILE";
        var filePath = app.Configuration[fileKey];
        if (string.IsNullOrEmpty(filePath)) return val;
        try { return File.ReadAllText(filePath).Trim(); }
        catch { return val; }
    }

    var config = app.Configuration;
    var placeholderPrefixes = new[] { "{", "PLACEHOLDER", "CHANGE_ME", "TODO" };
    var violations = new List<(string key, string value)>();

    void Check(string key, string label)
    {
        var val = ResolveSecret(key);
        if (string.IsNullOrEmpty(val) || placeholderPrefixes.Any(p => val.Contains(p, StringComparison.OrdinalIgnoreCase)))
            violations.Add((label, val ?? "(empty)"));
    }

    Check("Auth:ClientSecret", "OIDC ClientSecret");
    Check("Slack:BaseUrl", "Slack BaseUrl");
    Check("SweAf:BaseUrl", "SWE-AF BaseUrl");

    // Check model routing endpoints — any localhost endpoint in production is suspicious
    var routingConfig = config.GetSection("ModelRouting");
    foreach (var stage in Enum.GetValues<SDLC.Contracts.SdlcStage>())
    {
        var baseUrlKey = $"ModelRouting:StageEndpoints:{stage}:BaseUrl";
        var baseUrl = config[baseUrlKey];
        if (baseUrl != null && (baseUrl.Contains("localhost") || baseUrl.Contains("127.0.0.1")))
            violations.Add(($"ModelEndpoint {stage} (BaseUrl)", baseUrl));
    }

    if (violations.Count > 0)
    {
        var msg = $"Startup validation failed — missing or placeholder secrets:{string.Join("", violations.Select(v => $"\n  - {v.key}: {v.value}"))}";
        throw new InvalidOperationException(msg);
    }
}

// Run migrations, then initialize DB — WAL mode
using var initScope = app.Services.CreateScope();
await initScope.ServiceProvider.GetRequiredService<SDLC.Infrastructure.MigrationRunner>().RunAsync();
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
