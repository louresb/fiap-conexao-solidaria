using System.Security.Claims;

using ConexaoSolidaria.Contracts.Auth;
using ConexaoSolidaria.ServiceDefaults.Observability;
using ConexaoSolidaria.Web.Components;
using ConexaoSolidaria.Web.Services;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

using Prometheus;

using Serilog;
using Serilog.Events;
using Serilog.Sinks.Grafana.Loki;

using StackExchange.Redis;

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
    "web");

builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
})
.AddCookie(options =>
{
    options.Cookie.Name = "conexao-solidaria.session";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.ExpireTimeSpan = TimeSpan.FromHours(2);
    options.SlidingExpiration = true;
    options.LoginPath = "/acesso";
})
.AddOpenIdConnect(options =>
{
    options.Authority = builder.Configuration["Auth:Authority"]
        ?? throw new InvalidOperationException("Auth:Authority is required.");
    options.ClientId = builder.Configuration["Auth:ClientId"] ?? "conexao-web";
    options.ClientSecret = builder.Configuration["Auth:ClientSecret"];
    options.RequireHttpsMetadata = builder.Configuration.GetValue("Auth:RequireHttpsMetadata", true);
    if (!options.RequireHttpsMetadata)
    {
        options.CorrelationCookie.SecurePolicy = CookieSecurePolicy.None;
        options.CorrelationCookie.SameSite = SameSiteMode.Lax;
        options.NonceCookie.SecurePolicy = CookieSecurePolicy.None;
        options.NonceCookie.SameSite = SameSiteMode.Lax;
    }
    options.ResponseType = OpenIdConnectResponseType.Code;
    options.UsePkce = true;
    options.SaveTokens = true;
    options.GetClaimsFromUserInfoEndpoint = true;
    options.MapInboundClaims = false;
    options.Scope.Clear();
    options.Scope.Add("openid");
    options.Scope.Add("profile");
    options.Scope.Add("email");
    options.TokenValidationParameters.NameClaimType = "preferred_username";
    options.TokenValidationParameters.RoleClaimType = "roles";
    options.Events.OnTokenValidated = context =>
    {
        var accessToken = context.TokenEndpointResponse?.AccessToken;
        if (!string.IsNullOrWhiteSpace(accessToken) && context.Principal?.Identity is ClaimsIdentity identity)
        {
            identity.AddClaim(new Claim("access_token", accessToken));
        }

        return Task.CompletedTask;
    };
});

builder.Services.AddAuthorization();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var redisConnectionString = builder.Configuration.GetConnectionString("Redis");
if (!string.IsNullOrWhiteSpace(redisConnectionString))
{
    var redis = ConnectionMultiplexer.Connect(redisConnectionString);
    builder.Services.AddSingleton<IConnectionMultiplexer>(redis);
    builder.Services.AddDataProtection()
        .SetApplicationName("conexao-solidaria-web")
        .PersistKeysToStackExchangeRedis(redis, "conexao-solidaria:data-protection");
}
else
{
    builder.Services.AddDataProtection()
        .SetApplicationName("conexao-solidaria-web");
}

builder.Services.AddHttpClient<SolidariaApiClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Gateway:BaseUrl"] ?? "http://localhost:5000");
    client.Timeout = TimeSpan.FromSeconds(10);
});

var app = builder.Build();

app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
});

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();
app.UseHttpMetrics();

app.MapGet("/auth/login", (string? returnUrl) =>
{
    var destination = IsLocalReturnUrl(returnUrl) ? returnUrl! : "/";
    return Results.Challenge(
        new AuthenticationProperties { RedirectUri = destination },
        [OpenIdConnectDefaults.AuthenticationScheme]);
});

app.MapPost("/auth/logout", () => Results.SignOut(
    new AuthenticationProperties { RedirectUri = "/" },
    [CookieAuthenticationDefaults.AuthenticationScheme, OpenIdConnectDefaults.AuthenticationScheme]))
    .RequireAuthorization();

app.MapGet("/health/live", () => Results.Ok(new { status = "Healthy", service = "web" }));
app.MapGet("/health/ready", () => Results.Ok(new { status = "Healthy", dependencies = new[] { "gateway", "keycloak" } }));
app.MapMetrics();
app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

static bool IsLocalReturnUrl(string? returnUrl) =>
    !string.IsNullOrWhiteSpace(returnUrl)
    && returnUrl.StartsWith('/')
    && !returnUrl.StartsWith("//", StringComparison.Ordinal)
    && !returnUrl.StartsWith("/\\", StringComparison.Ordinal);

public partial class Program;
