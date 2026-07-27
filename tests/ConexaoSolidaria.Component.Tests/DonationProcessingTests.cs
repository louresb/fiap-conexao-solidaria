using System.Text.Json;

using ConexaoSolidaria.Campaigns.Data;
using ConexaoSolidaria.Campaigns.Infrastructure.Search;
using ConexaoSolidaria.Campaigns.Worker.Consumers;
using ConexaoSolidaria.Contracts.Campaigns;
using ConexaoSolidaria.Contracts.Events;
using ConexaoSolidaria.Donations.Api.Consumers;
using ConexaoSolidaria.Donations.Api.Data;

using MassTransit;
using MassTransit.Testing;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;

namespace ConexaoSolidaria.Component.Tests;

public sealed class DonationProcessingTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Confirmed_payment_updates_donation_once_and_preserves_correlation()
    {
        await using var host = await CreateDonationsHostAsync();
        var donation = await SeedDonationAsync(host, 75m);
        var consumer = host.Harness.GetConsumerHarness<PaymentConfirmedConsumer>();
        var payload = CreatePayment(donation.CampaignId, donation.Id, donation.Amount, donation.TenantId);
        var first = CreateEvent(EventTypes.PaymentConfirmed, payload.TenantId, "corr-donation-first", payload);
        var repeated = CreateEvent(EventTypes.PaymentConfirmed, payload.TenantId, "corr-donation-repeated", payload);

        await host.Harness.Bus.Publish(first);
        Assert.True(await consumer.Consumed.Any<IntegrationEvent>(x => x.Context.Message.EventId == first.EventId));
        await host.Harness.Bus.Publish(repeated);
        Assert.True(await consumer.Consumed.Any<IntegrationEvent>(x => x.Context.Message.EventId == repeated.EventId));

        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DonationsDbContext>();
        var stored = await db.Donations.SingleAsync(x => x.Id == donation.Id);
        var processedEvents = host.Harness.Published.Select<IntegrationEvent>()
            .Where(x => x.Context.Message.EventType == EventTypes.DonationProcessed)
            .ToList();

        Assert.Equal(DonationStatus.Confirmed, stored.Status);
        Assert.Single(processedEvents);
        Assert.Equal("corr-donation-first", processedEvents[0].Context.Message.CorrelationId);
        Assert.Equal(first.EventId.ToString(), processedEvents[0].Context.Message.CausationId);
    }

    [Fact]
    public async Task Donation_processed_updates_campaign_projection_and_publishes_goal_event()
    {
        await using var host = await CreateCampaignsHostAsync();
        var campaign = await SeedCampaignAsync(host, 950m);
        var consumer = host.Harness.GetConsumerHarness<DonationProcessedConsumer>();
        var readModelConsumer = host.Harness.GetConsumerHarness<CampaignChangedConsumer>();
        var cacheKey = $"active-campaigns:{campaign.TenantId}";
        await using (var setupScope = host.Services.CreateAsyncScope())
        {
            var cache = setupScope.ServiceProvider.GetRequiredService<IDistributedCache>();
            await cache.SetStringAsync(cacheKey, "stale-projection");
        }

        var payload = new DonationProcessedPayload(
            Guid.NewGuid(),
            campaign.Id,
            75m,
            campaign.TenantId);
        var message = CreateEvent(EventTypes.DonationProcessed, payload.TenantId, "corr-campaign-projection", payload);

        await host.Harness.Bus.Publish(message);
        Assert.True(await consumer.Consumed.Any<IntegrationEvent>(x => x.Context.Message.EventId == message.EventId));
        Assert.True(await readModelConsumer.Consumed.Any<IntegrationEvent>(
            x => x.Context.Message.EventType == EventTypes.CampaignProjectionUpdated));

        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CampaignsDbContext>();
        var stored = await db.Campaigns.SingleAsync(x => x.Id == campaign.Id);
        var publicDonation = await db.DonationProjections.SingleAsync(x => x.DonationId == payload.DonationId);
        var goalEvents = host.Harness.Published.Select<IntegrationEvent>()
            .Where(x => x.Context.Message.EventType == EventTypes.CampaignGoalReached)
            .ToList();
        var projectionEvents = host.Harness.Published.Select<IntegrationEvent>()
            .Where(x => x.Context.Message.EventType == EventTypes.CampaignProjectionUpdated)
            .ToList();
        var storedCache = await scope.ServiceProvider
            .GetRequiredService<IDistributedCache>()
            .GetStringAsync(cacheKey);
        var search = scope.ServiceProvider.GetRequiredService<RecordingSearchIndexer>();

        Assert.Equal(1025m, stored.TotalRaised);
        Assert.Equal(campaign.TenantId, publicDonation.TenantId);
        Assert.Equal(75m, publicDonation.Amount);
        Assert.Equal(message.OccurredAtUtc, publicDonation.ProcessedAtUtc);
        Assert.Single(goalEvents);
        Assert.Equal(message.CorrelationId, goalEvents[0].Context.Message.CorrelationId);
        Assert.Equal(message.EventId.ToString(), goalEvents[0].Context.Message.CausationId);
        Assert.Single(projectionEvents);
        Assert.Equal(message.CorrelationId, projectionEvents[0].Context.Message.CorrelationId);
        Assert.Equal(message.EventId.ToString(), projectionEvents[0].Context.Message.CausationId);
        Assert.Null(storedCache);
        Assert.Equal(1, search.IndexCount);
    }

    [Fact]
    public async Task Campaign_event_invalidates_cache_and_refreshes_search_projection()
    {
        await using var host = await CreateCampaignsHostAsync();
        var campaign = await SeedCampaignAsync(host, 120m);
        var consumer = host.Harness.GetConsumerHarness<CampaignChangedConsumer>();
        var cacheKey = $"active-campaigns:{campaign.TenantId}";

        await using (var setupScope = host.Services.CreateAsyncScope())
        {
            var cache = setupScope.ServiceProvider.GetRequiredService<IDistributedCache>();
            await cache.SetStringAsync(cacheKey, "stale-projection");
        }

        var payload = new CampaignDto(
            campaign.Id,
            campaign.TenantId,
            campaign.Title,
            campaign.Description,
            campaign.StartDate,
            campaign.EndDate,
            campaign.GoalAmount,
            campaign.TotalRaised,
            campaign.Status);
        var message = CreateEvent(EventTypes.CampaignPublished, campaign.TenantId, "corr-campaign-published", payload);

        await host.Harness.Bus.Publish(message);
        Assert.True(await consumer.Consumed.Any<IntegrationEvent>(x => x.Context.Message.EventId == message.EventId));

        await using var assertionScope = host.Services.CreateAsyncScope();
        var storedCache = await assertionScope.ServiceProvider
            .GetRequiredService<IDistributedCache>()
            .GetStringAsync(cacheKey);
        var search = assertionScope.ServiceProvider.GetRequiredService<RecordingSearchIndexer>();

        Assert.Null(storedCache);
        Assert.Equal(1, search.IndexCount);
    }

    [Fact]
    public async Task Payment_from_another_tenant_cannot_change_donation()
    {
        await using var host = await CreateDonationsHostAsync();
        var donation = await SeedDonationAsync(host, 50m);
        var consumer = host.Harness.GetConsumerHarness<PaymentConfirmedConsumer>();
        var payload = CreatePayment(donation.CampaignId, donation.Id, donation.Amount, "outro-tenant");
        var message = CreateEvent(EventTypes.PaymentConfirmed, payload.TenantId, "corr-cross-tenant", payload);

        await host.Harness.Bus.Publish(message);
        Assert.True(await consumer.Consumed.Any<IntegrationEvent>(x => x.Context.Message.EventId == message.EventId));

        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DonationsDbContext>();
        var stored = await db.Donations.SingleAsync(x => x.Id == donation.Id);

        Assert.Equal(DonationStatus.PendingPayment, stored.Status);
        Assert.DoesNotContain(
            host.Harness.Published.Select<IntegrationEvent>(),
            item => item.Context.Message.EventType == EventTypes.DonationProcessed);
    }

    private static async Task<MassTransitComponentHost> CreateDonationsHostAsync()
    {
        var host = await MassTransitComponentHost.CreateAsync(
            (services, connection) =>
                services.AddDbContext<DonationsDbContext>(options => options.UseSqlite(connection)),
            bus => bus.AddConsumer<PaymentConfirmedConsumer>());

        await using var scope = host.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<DonationsDbContext>().Database.EnsureCreatedAsync();
        return host;
    }

    private static async Task<MassTransitComponentHost> CreateCampaignsHostAsync()
    {
        var host = await MassTransitComponentHost.CreateAsync(
            (services, connection) =>
            {
                services.AddDbContext<CampaignsDbContext>(options => options.UseSqlite(connection));
                services.AddDistributedMemoryCache();
                services.AddSingleton<RecordingSearchIndexer>();
                services.AddSingleton<ICampaignSearchIndexer>(provider =>
                    provider.GetRequiredService<RecordingSearchIndexer>());
            },
            bus =>
            {
                bus.AddConsumer<DonationProcessedConsumer>();
                bus.AddConsumer<CampaignChangedConsumer>();
            });

        await using var scope = host.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<CampaignsDbContext>().Database.EnsureCreatedAsync();
        return host;
    }

    private static async Task<Donation> SeedDonationAsync(MassTransitComponentHost host, decimal amount)
    {
        var donation = new Donation
        {
            CampaignId = Guid.NewGuid(),
            TenantId = "esperanca-solidaria",
            DonorId = "donor-component",
            DonorEmail = "doador@example.org",
            Amount = amount,
            PaymentMethod = "pix"
        };

        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DonationsDbContext>();
        db.Donations.Add(donation);
        await db.SaveChangesAsync();
        return donation;
    }

    private static async Task<Campaign> SeedCampaignAsync(MassTransitComponentHost host, decimal totalRaised)
    {
        var campaign = new Campaign
        {
            TenantId = "esperanca-solidaria",
            Title = "Mesa Cheia nas Ferias",
            Description = "Seguranca alimentar para familias acompanhadas.",
            StartDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)),
            EndDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
            GoalAmount = 1000m,
            TotalRaised = totalRaised,
            Status = CampaignStatus.Ativa
        };

        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CampaignsDbContext>();
        db.Campaigns.Add(campaign);
        await db.SaveChangesAsync();
        return campaign;
    }

    private static PaymentConfirmedPayload CreatePayment(
        Guid campaignId,
        Guid donationId,
        decimal amount,
        string tenantId) => new(
            Guid.NewGuid(),
            donationId,
            campaignId,
            amount,
            "BRL",
            "component-sandbox",
            $"sandbox-{Guid.NewGuid():N}",
            DateTimeOffset.UtcNow,
            tenantId);

    private static IntegrationEvent CreateEvent<TPayload>(
        string eventType,
        string tenantId,
        string correlationId,
        TPayload payload) =>
        IntegrationEvent.Create(
            eventType,
            tenantId,
            correlationId,
            "component-tests",
            JsonSerializer.Serialize(payload, JsonOptions));

    private sealed class RecordingSearchIndexer : ICampaignSearchIndexer
    {
        private int _indexCount;

        public int IndexCount => _indexCount;

        public Task IndexAsync(Campaign campaign, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _indexCount);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ActiveCampaignDto>> SearchAsync(
            string tenantId,
            string query,
            int limit,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ActiveCampaignDto>>([]);
    }
}
