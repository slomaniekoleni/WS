using System.Threading.RateLimiting;
using Anthropic;
using Microsoft.EntityFrameworkCore;
using Serilog;
using Serilog.Formatting.Compact;
using Ws.Api;
using Ws.Api.Endpoints;
using Ws.Core.Data;
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

// Each chat message costs a Claude call: cap it per client IP.
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy(ChatEndpoints.RateLimitPolicy, http => RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 12, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
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
    app.Logger.LogInformation("Database ready at {Path}", dbPath);
    if (string.IsNullOrWhiteSpace(claudeKey)) app.Logger.LogWarning("Claude:ApiKey not set: AI chat is disabled");
}

app.UseSerilogRequestLogging();
// Malformed requests are the client's fault: 400, not 500.
app.UseExceptionHandler(new ExceptionHandlerOptions
{
    StatusCodeSelector = ex => ex is BadHttpRequestException bad ? bad.StatusCode : StatusCodes.Status500InternalServerError,
});
app.UseCors();
app.UseRateLimiter();
if (app.Environment.IsDevelopment()) app.MapOpenApi();

app.MapGet("/health", () => Results.Ok("ok"));
app.MapPublicApi();
app.MapChatApi();

// Built React site (web/dist -> wwwroot in the Docker image). Client-side routes fall back to index.html,
// but unknown /api/* paths stay 404s.
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapFallbackToFile("{*path:regex(^(?!api/).*$)}", "index.html");

app.Run();
