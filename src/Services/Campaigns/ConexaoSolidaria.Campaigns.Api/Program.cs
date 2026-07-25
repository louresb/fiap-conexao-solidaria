using System.Text.Json;
using System.Security.Claims;
using ConexaoSolidaria.Campaigns.Api.Search;
using ConexaoSolidaria.Campaigns.Data;
using ConexaoSolidaria.Contracts.Auth;
using ConexaoSolidaria.Contracts.Campaigns;
using ConexaoSolidaria.Contracts.Events;
using ConexaoSolidaria.Contracts.Validation;
using ConexaoSolidaria.Infrastructure.Http;
using MassTransit;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.IdentityModel.Tokens;
using Prometheus;
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

    bus.UsingRabbitMq((context, cfg) =>
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
builder.Services.AddOpenApi();

var app = builder.Build();
var enforceAuth = app.Configuration.GetValue("Auth:Enforce", false);

app.UseAuthentication();
app.UseCorrelationAndTenant();
app.UseAuthorization();
app.UseHttpMetrics();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapGet("/health/live", () => Results.Ok(new { status = "Healthy", service = "campaigns-api" }));
app.MapMetrics();
app.MapGet("/health/ready", async (CampaignsDbContext db, CancellationToken cancellationToken) =>
{
    await db.Database.ExecuteSqlRawAsync("SELECT 1", cancellationToken);
    return Results.Ok(new { status = "Healthy", dependencies = new[] { "postgres", "rabbitmq", "redis" } });
});

var management = app.MapGroup("/api/management/campaigns");
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
});

management.MapPost("/", async (
    CreateCampaignRequest request,
    CampaignsDbContext db,
    ICampaignSearchIndexer search,
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
    await publishEndpoint.Publish(
        IntegrationEvent.Create(
            EventTypes.CampaignCreated,
            tenantId,
            http.CorrelationId(),
            "campaigns-api",
            JsonSerializer.Serialize(ToDto(campaign), jsonOptions)),
        cancellationToken);

    await db.SaveChangesAsync(cancellationToken);
    await search.IndexAsync(campaign, cancellationToken);

    return Results.Created($"/api/management/campaigns/{campaign.Id}", ToDto(campaign));
});

management.MapPut("/{id:guid}", async (
    Guid id,
    UpdateCampaignRequest request,
    CampaignsDbContext db,
    ICampaignSearchIndexer search,
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

    await db.SaveChangesAsync(cancellationToken);
    await search.IndexAsync(campaign, cancellationToken);

    return Results.Ok(ToDto(campaign));
});

management.MapDelete("/{id:guid}", async (
    Guid id,
    CampaignsDbContext db,
    ICampaignSearchIndexer search,
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
    await search.IndexAsync(campaign, cancellationToken);
    return Results.NoContent();
});

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
});

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
});

var donations = app.MapGroup("/api/donations");
if (enforceAuth)
{
    donations.RequireAuthorization("DonorsOnly");
}

donations.MapPost("/", async (
    DonationIntentRequest request,
    CampaignsDbContext db,
    IPublishEndpoint publishEndpoint,
    HttpContext http,
    CancellationToken cancellationToken) =>
{
    if (request.Amount <= 0)
    {
        return Results.BadRequest(new { error = "Valor da doacao deve ser maior que zero." });
    }

    var paymentMethod = request.PaymentMethod.Trim().ToLowerInvariant();
    if (paymentMethod is not ("pix" or "credit_card" or "bank_slip"))
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["paymentMethod"] = ["Use pix, credit_card ou bank_slip."]
        });
    }

    var tenantId = http.TenantId();
    var campaign = await db.Campaigns.FirstOrDefaultAsync(c => c.Id == request.CampaignId && c.TenantId == tenantId, cancellationToken);
    if (campaign is null)
    {
        return Results.NotFound(new { error = "Campanha nao encontrada." });
    }

    if (!CampaignRules.CanReceiveDonation(campaign.Status, campaign.StartDate, campaign.EndDate))
    {
        return Results.BadRequest(new { error = "Nao e possivel doar para campanhas concluidas ou canceladas." });
    }

    var donorId = http.User.FindFirstValue("sub") ?? request.DonorId;
    var donorEmail = http.User.FindFirstValue("email") ?? request.DonorEmail;
    if (string.IsNullOrWhiteSpace(donorId) || string.IsNullOrWhiteSpace(donorEmail))
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["donor"] = ["A identidade autenticada do doador é obrigatória."]
        });
    }

    var donation = new Donation
    {
        CampaignId = campaign.Id,
        TenantId = tenantId,
        DonorId = donorId,
        DonorEmail = donorEmail,
        Amount = request.Amount,
        PaymentMethod = paymentMethod,
        Status = "Pending"
    };

    db.Donations.Add(donation);
    var payload = new DonationIntentCreatedPayload(
        donation.Id,
        campaign.Id,
        donation.DonorId,
        donation.DonorEmail,
        donation.Amount,
        tenantId,
        paymentMethod);

    await publishEndpoint.Publish(
        IntegrationEvent.Create(
            EventTypes.DonationIntentCreated,
            tenantId,
            http.CorrelationId(),
            "campaigns-api",
            JsonSerializer.Serialize(payload, jsonOptions)),
        cancellationToken);

    await db.SaveChangesAsync(cancellationToken);

    return Results.Accepted($"/api/donations/{donation.Id}", new
    {
        donation.Id,
        donation.Status,
        message = "Doacao recebida e enviada para processamento assincrono."
    });
});

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<CampaignsDbContext>();
    await CampaignsDbContext.SeedDemoDataAsync(db);
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
