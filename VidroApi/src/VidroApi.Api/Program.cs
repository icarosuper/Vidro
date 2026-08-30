using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using VidroApi.Api.BackgroundServices;
using VidroApi.Api.Extensions;
using VidroApi.Api.Middleware;
using VidroApi.Api;
using VidroApi.Application;
using VidroApi.Infrastructure;
using VidroApi.Infrastructure.Persistence;
using VidroApi.Infrastructure.Settings;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, cfg) => cfg.ReadFrom.Configuration(ctx.Configuration));

// Application + Infrastructure
builder.Services.AddMediator(options => options.ServiceLifetime = ServiceLifetime.Scoped);
builder.Services.AddApplication(typeof(Program).Assembly);
builder.Services.AddInfrastructure(builder.Configuration);

// Settings validation
builder.Services.AddSettings();

// JWT Authentication
var jwtSettings = builder.Configuration.GetSection("Jwt").Get<JwtSettings>()!;
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(opts =>
    {
        opts.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.Secret)),
            ValidateIssuer = false,
            ValidateAudience = false,
            ClockSkew = TimeSpan.Zero
        };
    });

builder.Services.AddAuthorization();

// Browser calls the API cross-origin (front on :3000, API on :5000). Bearer token
// travels in a header, not a cookie, so no AllowCredentials is needed.
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
    policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod()));
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();

// Credential endpoints are the cheap target for brute force, so they get a per-IP budget.
// Everything else stays unlimited — a global limiter would throttle legitimate browsing.
var rateLimits = builder.Configuration.GetSection("RateLimit").Get<RateLimitSettings>()!;
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(RateLimitSettings.AuthPolicy, http =>
        RateLimitPartition.GetFixedWindowLimiter(
            // Behind a proxy this is the proxy's IP until ForwardedHeaders is configured.
            http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = rateLimits.AuthPermitLimit,
                Window = TimeSpan.FromSeconds(rateLimits.AuthWindowSeconds)
            }));
});
builder.Services.AddHostedService<VideoReconciliationService>();
builder.Services.AddHostedService<StorageCleanupService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

// Pending migrations are applied on every startup, in every environment: deploying the
// image is the whole deploy. Note this makes the schema change before the old instance
// stops, so migrations must stay backward-compatible with the running version, and two
// instances booting at once both try to migrate (EF takes a lock, the loser waits).
using (var scope = app.Services.CreateScope())
    await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();

// CorrelationId must come first so the ID is in scope for all subsequent logs,
// including Serilog's own request log entry.
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseSerilogRequestLogging();

app.UseMiddleware<ExceptionMiddleware>();
// Before authentication so 401 responses also carry the CORS headers — otherwise the
// browser reports an opaque CORS error instead of the real status.
app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapAllEndpoints();
// Liveness only — no dependency probes, so a flapping Redis can't take the container down.
app.MapHealthChecks("/health");

if (allowedOrigins.Length == 0)
    Log.Warning("Cors:AllowedOrigins is empty — every cross-origin browser request will be blocked.");

app.Lifetime.ApplicationStarted.Register(() =>
    Log.Information("Application listening on: {Urls}", string.Join(", ", app.Urls)));

app.Run();

public partial class Program;
