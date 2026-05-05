using SDLC.Dashboard.Components;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var dbConn = builder.Configuration.GetConnectionString("SDLCDb") ?? "Data Source=sdlc.db";
var artifactDir = Path.Combine(AppContext.BaseDirectory, "artifacts");
builder.Services.AddSingleton<SDLC.Infrastructure.IArtifactStore>(
    new SDLC.Infrastructure.ArtifactStore(dbConn, artifactDir));
builder.Services.AddSingleton<SDLC.Infrastructure.IStageGateStore>(
    new SDLC.Infrastructure.StageGateStore(dbConn));

// HTTP client and notification service
builder.Services.AddHttpClient();
builder.Services.AddSingleton<SDLC.Notifications.DashboardUrlBuilder>();
builder.Services.AddSingleton<SDLC.Notifications.INotificationService>(sp =>
    new SDLC.Notifications.SlackNotificationService(
        sp.GetRequiredService<IHttpClientFactory>().CreateClient(),
        "/webhook/sdlc",
        sp.GetRequiredService<SDLC.Notifications.DashboardUrlBuilder>()));

// Simple run service — renders data, resolves gates via StageGateStore
builder.Services.AddScoped<SDLC.Dashboard.Services.ISdlcRunService, SDLC.Dashboard.Services.SdlcRunService>();

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
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
