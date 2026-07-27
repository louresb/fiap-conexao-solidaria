using System.Text.Json;

using ConexaoSolidaria.Campaigns.Data;
using ConexaoSolidaria.Campaigns.Infrastructure.Search;
using ConexaoSolidaria.Contracts.Auth;
using ConexaoSolidaria.Contracts.Campaigns;
using ConexaoSolidaria.Contracts.Events;
using ConexaoSolidaria.Contracts.Validation;
using ConexaoSolidaria.ServiceDefaults.Http;
using ConexaoSolidaria.ServiceDefaults.Observability;
using ConexaoSolidaria.ServiceDefaults.OpenApi;

using MassTransit;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.IdentityModel.Tokens;

using Prometheus;

using Scalar.AspNetCore;

using Serilog;
using Serilog.Events;
using Serilog.Sinks.Grafana.Loki;

var builder = WebApplication.CreateBuilder(args);
var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);

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
    "campaigns-api");

builder.Services.AddDbContext<CampaignsDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("CampaignsDb")));

var redisConnection = builder.Configuration.GetConnectionString("Redis");
if (string.IsNullOrWhiteSpace(redisConnection))
{
    builder.Services.AddDistributedMemoryCache();
}
else
{
    builder.Services.AddStackExchangeRedisCache(options => options.Configuration = redisConnection);
}

builder.Services.Configure<OpenSearchOptions>(builder.Configuration.GetSection("OpenSearch"));
builder.Services.AddHttpClient<OpenSearchCampaignSearchIndexer>();
builder.Services.AddScoped<ICampaignSearchIndexer, OpenSearchCampaignSearchIndexer>();

builder.Services.AddMassTransit(bus =>
{
    bus.AddEntityFrameworkOutbox<CampaignsDbContext>(outbox =>
    {
        outbox.UsePostgres();
        outbox.UseBusOutbox();
    });

    bus.UsingRabbitMq((_, cfg) =>
    {
        cfg.Host(
            builder.Configuration["RabbitMq:Host"] ?? "localhost",
            builder.Configuration["RabbitMq:VirtualHost"] ?? "/",
            host =>
            {
                host.Username(builder.Configuration["RabbitMq:Username"] ?? "guest");
                host.Password(builder.Configuration["RabbitMq:Password"] ?? "guest");
            });

    });
});

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        var authority = builder.Configuration["Auth:Authority"]
            ?? throw new InvalidOperationException("Auth:Authority is required.");
        var audience = builder.Configuration["Auth:Audience"] ?? "conexao-solidaria";
        options.MapInboundClaims = false;
        options.Authority = authority;
        options.Audience = audience;
        options.RequireHttpsMetadata = false;
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

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("ManagersOnly", policy => policy.RequireRole(AuthDefaults.ManagerRole));
    options.AddPolicy("DonorsOnly", policy => policy.RequireRole(AuthDefaults.DonorRole));
});
builder.Services.AddConexaoSolidariaOpenApi(
    "Conexao Solidaria - Campaigns API",
    "Gestao de campanhas, catalogo publico, busca e projecoes de transparencia.");

var app = builder.Build();
var enforceAuth = app.Configuration.GetValue("Auth:Enforce", false);

app.UseAuthentication();
app.UseCorrelationAndTenant();
app.UseAuthorization();
app.UseHttpMetrics();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.MapGet("/health/live", () => Results.Ok(new { status = "Healthy", service = "campaigns-api" }))
    .WithTags("Operacao")
    .WithName("CampaignsLiveness")
    .WithSummary("Verifica se a Campaigns API esta em execucao.");
app.MapMetrics();
app.MapGet("/health/ready", async (CampaignsDbContext db, CancellationToken cancellationToken) =>
{
    await db.Database.ExecuteSqlRawAsync("SELECT 1", cancellationToken);
    return Results.Ok(new { status = "Healthy", dependencies = new[] { "postgres", "rabbitmq", "redis" } });
})
    .WithTags("Operacao")
    .WithName("CampaignsReadiness")
    .WithSummary("Verifica as dependencias criticas da Campaigns API.");

var management = app.MapGroup("/api/management/campaigns").WithTags("Gestao de campanhas");
if (enforceAuth)
{
    management.RequireAuthorization("ManagersOnly");
}

management.MapGet("/", async (CampaignsDbContext db, HttpContext http, CancellationToken cancellationToken) =>
{
    var tenantId = http.TenantId();
    var campaigns = await db.Campaigns
        .AsNoTracking()
        .Where(c => c.TenantId == tenantId)
        .OrderByDescending(c => c.CreatedAtUtc)
        .Select(c => new CampaignDto(
            c.Id,
            c.TenantId,
            c.Title,
            c.Description,
            c.StartDate,
            c.EndDate,
            c.GoalAmount,
            c.TotalRaised,
            c.Status))
        .ToListAsync(cancellationToken);

    return Results.Ok(campaigns);
})
    .WithName("ListManagedCampaigns")
    .WithSummary("Lista todas as campanhas do tenant autenticado.");

management.MapPost("/", async (
    CreateCampaignRequest request,
    CampaignsDbContext db,
    IPublishEndpoint publishEndpoint,
    HttpContext http,
    CancellationToken cancellationToken) =>
{
    var tenantId = http.TenantId();
    var errors = CampaignRules.Validate(request.Title, request.Description, request.StartDate, request.EndDate, request.GoalAmount);
    if (errors.Count > 0)
    {
        return Results.BadRequest(new { errors });
    }

    if (request.Status != CampaignStatus.Rascunho)
    {
        return Results.Conflict(new
        {
            error = "Uma campanha deve ser criada como rascunho antes de seguir para revisao."
        });
    }

    var campaign = new Campaign
    {
        TenantId = tenantId,
        Title = request.Title.Trim(),
        Description = request.Description.Trim(),
        StartDate = request.StartDate,
        EndDate = request.EndDate,
        GoalAmount = request.GoalAmount,
        Status = request.Status
    };

    db.Campaigns.Add(campaign);
    var campaignPayload = JsonSerializer.Serialize(ToDto(campaign), jsonOptions);
    await publishEndpoint.Publish(
        IntegrationEvent.Create(
            EventTypes.CampaignCreated,
            tenantId,
            http.CorrelationId(),
            "campaigns-api",
            campaignPayload),
        cancellationToken);

    await db.SaveChangesAsync(cancellationToken);

    return Results.Created($"/api/management/campaigns/{campaign.Id}", ToDto(campaign));
})
    .WithName("CreateCampaign")
    .WithSummary("Cria uma campanha e publica seu evento de dominio.");

management.MapPut("/{id:guid}", async (
    Guid id,
    UpdateCampaignRequest request,
    CampaignsDbContext db,
    IPublishEndpoint publishEndpoint,
    HttpContext http,
    CancellationToken cancellationToken) =>
{
    var tenantId = http.TenantId();
    var campaign = await db.Campaigns.FirstOrDefaultAsync(c => c.Id == id && c.TenantId == tenantId, cancellationToken);
    if (campaign is null)
    {
        return Results.NotFound();
    }

    var errors = CampaignRules.Validate(request.Title, request.Description, request.StartDate, request.EndDate, request.GoalAmount);
    if (errors.Count > 0)
    {
        return Results.BadRequest(new { errors });
    }

    if (!CampaignRules.CanTransition(campaign.Status, request.Status))
    {
        return Results.Conflict(new
        {
            error = $"Transicao de {campaign.Status} para {request.Status} nao e permitida.",
            allowedStatuses = CampaignRules.NextStatuses(campaign.Status)
        });
    }

    var previousStatus = campaign.Status;
    campaign.Title = request.Title.Trim();
    campaign.Description = request.Description.Trim();
    campaign.StartDate = request.StartDate;
    campaign.EndDate = request.EndDate;
    campaign.GoalAmount = request.GoalAmount;
    campaign.Status = request.Status;
    campaign.UpdatedAtUtc = DateTimeOffset.UtcNow;

    await publishEndpoint.Publish(
        IntegrationEvent.Create(
            EventTypes.CampaignUpdated,
            tenantId,
            http.CorrelationId(),
            "campaigns-api",
            JsonSerializer.Serialize(ToDto(campaign), jsonOptions)),
        cancellationToken);

    if (previousStatus != CampaignStatus.Ativa && campaign.Status == CampaignStatus.Ativa)
    {
        await publishEndpoint.Publish(
            IntegrationEvent.Create(
                EventTypes.CampaignPublished,
                tenantId,
                http.CorrelationId(),
                "campaigns-api",
                JsonSerializer.Serialize(ToDto(campaign), jsonOptions)),
            cancellationToken);
    }

    await db.SaveChangesAsync(cancellationToken);

    return Results.Ok(ToDto(campaign));
})
    .WithName("UpdateCampaign")
    .WithSummary("Atualiza uma campanha e sua projecao de busca.");

management.MapDelete("/{id:guid}", async (
    Guid id,
    CampaignsDbContext db,
    IPublishEndpoint publishEndpoint,
    HttpContext http,
    CancellationToken cancellationToken) =>
{
    var tenantId = http.TenantId();
    var campaign = await db.Campaigns.FirstOrDefaultAsync(
        item => item.Id == id && item.TenantId == tenantId,
        cancellationToken);
    if (campaign is null)
    {
        return Results.NotFound();
    }

    if (campaign.Status == CampaignStatus.Cancelada)
    {
        return Results.NoContent();
    }

    if (!CampaignRules.CanTransition(campaign.Status, CampaignStatus.Cancelada))
    {
        return Results.Conflict(new { error = $"A campanha em estado {campaign.Status} nao pode ser cancelada." });
    }

    campaign.Status = CampaignStatus.Cancelada;
    campaign.UpdatedAtUtc = DateTimeOffset.UtcNow;
    await publishEndpoint.Publish(
        IntegrationEvent.Create(
            EventTypes.CampaignCancelled,
            tenantId,
            http.CorrelationId(),
            "campaigns-api",
            JsonSerializer.Serialize(ToDto(campaign), jsonOptions)),
        cancellationToken);

    await db.SaveChangesAsync(cancellationToken);
    return Results.NoContent();
})
    .WithName("CancelCampaign")
    .WithSummary("Cancela logicamente uma campanha do tenant atual.");

app.MapGet("/api/public/campaigns", async (
    string? tenantId,
    CampaignsDbContext db,
    IDistributedCache cache,
    HttpContext http,
    CancellationToken cancellationToken) =>
{
    var resolvedTenantId = tenantId ?? http.TenantId();
    var cacheKey = $"active-campaigns:{resolvedTenantId}";
    var cached = await cache.GetStringAsync(cacheKey, cancellationToken);
    if (!string.IsNullOrWhiteSpace(cached))
    {
        http.Response.Headers["X-Cache"] = "HIT";
        return Results.Text(cached, "application/json");
    }

    var campaigns = await db.Campaigns
        .AsNoTracking()
        .Where(c => c.TenantId == resolvedTenantId && c.Status == CampaignStatus.Ativa)
        .OrderBy(c => c.EndDate)
        .Select(c => new ActiveCampaignDto(
            c.Id,
            c.TenantId,
            c.Title,
            c.Description,
            c.GoalAmount,
            c.TotalRaised,
            c.GoalAmount <= 0 ? 0 : Math.Round(c.TotalRaised / c.GoalAmount * 100, 2),
            c.EndDate))
        .ToListAsync(cancellationToken);

    var json = JsonSerializer.Serialize(campaigns, jsonOptions);
    await cache.SetStringAsync(
        cacheKey,
        json,
        new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(30) },
        cancellationToken);

    http.Response.Headers["X-Cache"] = "MISS";
    return Results.Text(json, "application/json");
})
    .WithTags("Campanhas publicas")
    .WithName("ListActiveCampaigns")
    .WithSummary("Lista campanhas ativas com projecao cacheada de transparencia.");

app.MapGet("/api/public/campaigns/{id:guid}", async (
    Guid id,
    CampaignsDbContext db,
    HttpContext http,
    CancellationToken cancellationToken) =>
{
    var campaign = await db.Campaigns
        .AsNoTracking()
        .FirstOrDefaultAsync(
            item => item.Id == id
                && item.TenantId == http.TenantId()
                && item.Status == CampaignStatus.Ativa,
            cancellationToken);
    return campaign is null ? Results.NotFound() : Results.Ok(ToDto(campaign));
})
    .WithTags("Campanhas publicas")
    .WithName("GetPublicCampaign")
    .WithSummary("Consulta uma campanha do tenant para validacao e transparencia.");

app.MapGet("/api/public/transparency", async (
    string? tenantId,
    int? limit,
    CampaignsDbContext db,
    HttpContext http,
    CancellationToken cancellationToken) =>
{
    var resolvedTenantId = tenantId ?? http.TenantId();
    var take = Math.Clamp(limit ?? 12, 1, 30);
    var campaigns = await db.Campaigns
        .AsNoTracking()
        .Where(c => c.TenantId == resolvedTenantId && c.Status == CampaignStatus.Ativa)
        .Select(c => new { c.Id, c.Title, c.GoalAmount, c.TotalRaised })
        .ToListAsync(cancellationToken);
    var recentDonations = await (
        from donation in db.DonationProjections.AsNoTracking()
        join campaign in db.Campaigns.AsNoTracking() on donation.CampaignId equals campaign.Id
        where donation.TenantId == resolvedTenantId && campaign.TenantId == resolvedTenantId
        orderby donation.ProcessedAtUtc descending
        select new PublicDonationDto(
            donation.DonationId,
            donation.CampaignId,
            campaign.Title,
            donation.Amount,
            donation.ProcessedAtUtc))
        .Take(take)
        .ToListAsync(cancellationToken);
    var totalRaised = campaigns.Sum(c => c.TotalRaised);
    var publishedGoal = campaigns.Sum(c => c.GoalAmount);
    var averageProgress = campaigns.Count == 0
        ? 0
        : campaigns.Average(c => c.GoalAmount <= 0 ? 0 : Math.Min(c.TotalRaised / c.GoalAmount * 100, 100));

    return Results.Ok(new TransparencySnapshotDto(
        resolvedTenantId,
        totalRaised,
        publishedGoal,
        Math.Round(averageProgress, 2),
        campaigns.Count,
        recentDonations,
        DateTimeOffset.UtcNow));
})
    .WithTags("Campanhas publicas")
    .WithName("GetPublicTransparency")
    .WithSummary("Consulta a projecao publica de arrecadacao e doacoes anonimizadas.");

app.MapGet("/api/public/campaigns/search", async (
    string q,
    string? tenantId,
    int? limit,
    CampaignsDbContext db,
    ICampaignSearchIndexer search,
    HttpContext http,
    CancellationToken cancellationToken) =>
{
    var resolvedTenantId = tenantId ?? http.TenantId();
    var take = Math.Clamp(limit ?? 10, 1, 25);
    var results = await search.SearchAsync(resolvedTenantId, q, take, cancellationToken);

    if (results.Count == 0)
    {
        var normalized = q.Trim().ToLowerInvariant();
        results = await db.Campaigns
            .AsNoTracking()
            .Where(c => c.TenantId == resolvedTenantId && c.Status == CampaignStatus.Ativa)
            .Where(c => c.Title.ToLower().Contains(normalized) || c.Description.ToLower().Contains(normalized))
            .OrderByDescending(c => c.TotalRaised)
            .Take(take)
            .Select(c => new ActiveCampaignDto(
                c.Id,
                c.TenantId,
                c.Title,
                c.Description,
                c.GoalAmount,
                c.TotalRaised,
                c.GoalAmount <= 0 ? 0 : Math.Round(c.TotalRaised / c.GoalAmount * 100, 2),
                c.EndDate))
            .ToListAsync(cancellationToken);
    }

    return Results.Ok(results);
})
    .WithTags("Campanhas publicas")
    .WithName("SearchCampaigns")
    .WithSummary("Pesquisa campanhas por titulo e descricao com tolerancia a erros.");

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<CampaignsDbContext>();
    await CampaignsDbContext.SeedDemoDataAsync(db);
    var search = scope.ServiceProvider.GetRequiredService<ICampaignSearchIndexer>();
    var seededCampaigns = await db.Campaigns.AsNoTracking().ToListAsync();
    foreach (var campaign in seededCampaigns)
    {
        await search.IndexAsync(campaign, CancellationToken.None);
    }
}

app.Run();

static CampaignDto ToDto(Campaign campaign)
{
    return new CampaignDto(
        campaign.Id,
        campaign.TenantId,
        campaign.Title,
        campaign.Description,
        campaign.StartDate,
        campaign.EndDate,
        campaign.GoalAmount,
        campaign.TotalRaised,
        campaign.Status);
}
