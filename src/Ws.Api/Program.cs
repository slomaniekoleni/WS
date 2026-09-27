using System.Threading.RateLimiting;
using Anthropic;
using Microsoft.EntityFrameworkCore;
using Serilog;
using Serilog.Formatting.Compact;
using Microsoft.Extensions.Options;
using Ws.Api;
using Ws.Api.Admin;
using Ws.Api.Endpoints;
using Ws.Api.Telegram;
using Ws.Core.Data;
using Ws.Core.Notifications;
using Ws.Core.Receptionist;
using Ws.Core.Scheduling;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<WsOptions>(builder.Configuration.GetSection(WsOptions.Section));
var ws = builder.Configuration.GetSection(WsOptions.Section).Get<WsOptions>() ?? new WsOptions();

builder.Services.AddSerilog(cfg =>
{
    cfg.ReadFrom.Configuration(builder.Configuration).Enrich.FromLogContext();
    if (ws.LogJson) cfg.WriteTo.Console(new CompactJsonFormatter());
    else cfg.WriteTo.Console(outputTemplate: "{Timestamp:HH:mm:ss} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}");
});

var dbPath = Path.GetFullPath(ws.DatabasePath);
Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
builder.Services.AddDbContext<WsDbContext>(o => o.UseSqlite($"Data Source={dbPath}"));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<BookingService>();

// AI receptionist. Without Claude:ApiKey (user-secrets locally, Claude__ApiKey env on servers) the site works
// and the chat endpoint answers 503.
builder.Services.Configure<ReceptionistOptions>(builder.Configuration.GetSection(ReceptionistOptions.Section));
var claudeKey = builder.Configuration["Claude:ApiKey"];
if (!string.IsNullOrWhiteSpace(claudeKey))
{
    builder.Services.AddSingleton(new AnthropicClient { ApiKey = claudeKey });
    builder.Services.AddScoped<ReceptionistTools>();
    builder.Services.AddScoped<Receptionist>();
}

// Telegram: client bot, staff group (booking approvals, AI handoffs), reminders. Off without Telegram:BotToken.
builder.Services.Configure<TelegramOptions>(builder.Configuration.GetSection(TelegramOptions.Section));
builder.Services.AddScoped<Reminders>();
var telegramToken = builder.Configuration["Telegram:BotToken"];
if (!string.IsNullOrWhiteSpace(telegramToken))
{
    // Long polling holds requests for up to PollTimeoutSeconds, so the client timeout must be longer.
    builder.Services.AddHttpClient(TelegramApi.HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(90));
    builder.Services.AddSingleton<TelegramApi>();
    builder.Services.AddSingleton<TelegramStaffNotifier>();
    builder.Services.AddSingleton<IStaffNotifier>(sp => sp.GetRequiredService<TelegramStaffNotifier>());
    builder.Services.AddHostedService(sp => sp.GetRequiredService<TelegramStaffNotifier>());
    builder.Services.AddHostedService<TelegramBot>();
    builder.Services.AddHostedService<ReminderWorker>();
}
else
{
    builder.Services.AddSingleton<IStaffNotifier>(NullStaffNotifier.Instance);
}

builder.Services.AddScoped<ClientNotifier>();

// Staff admin panel: cookie login; first owner from Admin:Login / Admin:Password.
builder.Services.Configure<AdminBootstrapOptions>(builder.Configuration.GetSection(AdminBootstrapOptions.Section));
builder.Services.AddStaffAuth();

// Each chat message costs a Claude call: cap it per client IP. Logins are capped against password guessing.
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy(ChatEndpoints.RateLimitPolicy, http => RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 12, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    o.AddPolicy(StaffAuth.LoginRateLimitPolicy, http => RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(5), QueueLimit = 0 }));
});

builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.WithOrigins(ws.CorsOrigins).AllowAnyHeader().AllowAnyMethod()));
builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<WsDbContext>();
    await db.Database.MigrateAsync();
    await SeedData.EnsureSeededAsync(db);
    await StaffAuth.EnsureOwnerAsync(db, scope.ServiceProvider.GetRequiredService<IOptions<AdminBootstrapOptions>>().Value,
        ws.SalonId, TimeProvider.System, app.Logger);
    app.Logger.LogInformation("Database ready at {Path}", dbPath);
    if (string.IsNullOrWhiteSpace(claudeKey)) app.Logger.LogWarning("Claude:ApiKey not set: AI chat is disabled");
    if (string.IsNullOrWhiteSpace(telegramToken)) app.Logger.LogWarning("Telegram:BotToken not set: Telegram bot, staff notifications and reminders are off");
}

app.UseSerilogRequestLogging();
// Malformed requests are the client's fault: 400, not 500.
app.UseExceptionHandler(new ExceptionHandlerOptions
{
    StatusCodeSelector = ex => ex is BadHttpRequestException bad ? bad.StatusCode : StatusCodes.Status500InternalServerError,
});
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
if (app.Environment.IsDevelopment()) app.MapOpenApi();

app.MapGet("/health", () => Results.Ok("ok"));
app.MapPublicApi();
app.MapChatApi();
app.MapStaffAuth();
app.MapAdminApi();

// Built React site (web/dist -> wwwroot in the Docker image). Client-side routes fall back to index.html,
// but unknown /api/* paths stay 404s.
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapFallbackToFile("{*path:regex(^(?!api/).*$)}", "index.html");

app.Run();

// Lets integration tests (WebApplicationFactory<Program>) reach the app.
public partial class Program;