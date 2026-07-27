using System.Threading.RateLimiting;

using ConexaoSolidaria.Contracts.Auth;
using ConexaoSolidaria.ServiceDefaults.Http;
using ConexaoSolidaria.ServiceDefaults.Observability;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

using Prometheus;

using Serilog;
using Serilog.Events;
using Serilog.Sinks.Grafana.Loki;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, configuration) =>
{
    configuration
        .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
        .Enrich.FromLogContext()
        .WriteTo.Console();

    var lokiUrl = context.Configuration["Observability:LokiUrl"];
    if (!string.IsNullOrWhiteSpace(lokiUrl))
    {
        configuration.WriteTo.GrafanaLoki(lokiUrl);
    }
});

builder.Services.AddConexaoSolidariaTelemetry(
    builder.Configuration,
    builder.Environment,
    "gateway");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        var authority = builder.Configuration["Auth:Authority"]
            ?? throw new InvalidOperationException("Auth:Authority is required.");
        var audience = builder.Configuration["Auth:Audience"] ?? "conexao-solidaria";
        options.MapInboundClaims = false;
        options.Authority = authority;
        options.Audience = audience;
        options.RequireHttpsMetadata = builder.Configuration.GetValue("Auth:RequireHttpsMetadata", true);
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateAudience = true,
            ValidAudience = audience,
            ValidateIssuer = true,
            ValidIssuer = builder.Configuration["Auth:Issuer"] ?? authority,
            RoleClaimType = "roles",
            NameClaimType = "preferred_username"
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddRateLimiter(options =>
{
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
    {
        var tenantId = context.User.FindFirst(AuthDefaults.TenantClaim)?.Value
            ?? context.Request.Headers[CorrelationTenantMiddleware.TenantHeader].FirstOrDefault()
            ?? context.Request.Query["tenantId"].FirstOrDefault()
            ?? "anonymous";

        return RateLimitPartition.GetFixedWindowLimiter(
            tenantId,
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 120,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 20,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst
            });
    });
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});

builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));
builder.Services.AddHttpClient("readiness", client => client.Timeout = TimeSpan.FromSeconds(3));

var app = builder.Build();

app.UseAuthentication();
app.UseCorrelationAndTenant();
app.UseAuthorization();
app.UseRateLimiter();
app.UseHttpMetrics();
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    await next();
});

app.MapGet("/health/live", () => Results.Ok(new { status = "Healthy", service = "gateway" }));
app.MapMetrics();
app.MapGet("/health/ready", async (
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    CancellationToken cancellationToken) =>
{
    var dependencies = configuration.GetSection("Readiness:Dependencies").Get<Dictionary<string, string>>() ?? [];
    var client = httpClientFactory.CreateClient("readiness");
    var checks = await Task.WhenAll(dependencies.Select(async dependency =>
    {
        try
        {
            using var response = await client.GetAsync(dependency.Value, cancellationToken);
            return new { name = dependency.Key, healthy = response.IsSuccessStatusCode };
        }
        catch (HttpRequestException)
        {
            return new { name = dependency.Key, healthy = false };
        }
        catch (TaskCanceledException)
        {
            return new { name = dependency.Key, healthy = false };
        }
    }));

    var healthy = checks.All(check => check.healthy);
    return Results.Json(
        new { status = healthy ? "Healthy" : "Unhealthy", dependencies = checks },
        statusCode: healthy ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable);
});
app.MapReverseProxy();

app.Run();

public partial class Program;
