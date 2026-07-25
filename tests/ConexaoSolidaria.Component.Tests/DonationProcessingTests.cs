using System.Text.Json;
using ConexaoSolidaria.Campaigns.Data;
using ConexaoSolidaria.Contracts.Campaigns;
using ConexaoSolidaria.Contracts.Events;
using ConexaoSolidaria.Donations.Worker.Consumers;
using MassTransit;
using MassTransit.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ConexaoSolidaria.Component.Tests;

public sealed class DonationProcessingTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Confirmed_payment_updates_campaign_once_and_preserves_correlation()
    {
        await using var host = await CreateHostAsync();
        var (campaignId, donationId) = await SeedDonationAsync(host, 950m, 75m);
        var consumer = host.Harness.GetConsumerHarness<PaymentConfirmedConsumer>();
        var payload = CreatePayment(campaignId, donationId, 75m, "esperanca-solidaria");
        var first = CreateEvent(payload, "corr-donation-first");
        var repeated = CreateEvent(payload, "corr-donation-repeated");

        await host.Harness.Bus.Publish(first);
        Assert.True(await consumer.Consumed.Any<IntegrationEvent>(x => x.Context.Message.EventId == first.EventId));
        await host.Harness.Bus.Publish(repeated);
        Assert.True(await consumer.Consumed.Any<IntegrationEvent>(x => x.Context.Message.EventId == repeated.EventId));

        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CampaignsDbContext>();
        var campaign = await db.Campaigns.SingleAsync(x => x.Id == campaignId);
        var donation = await db.Donations.SingleAsync(x => x.Id == donationId);
        var processedEvents = host.Harness.Published.Select<IntegrationEvent>()
            .Where(x => x.Context.Message.EventType == EventTypes.DonationProcessed)
            .ToList();
        var goalEvents = host.Harness.Published.Select<IntegrationEvent>()
            .Where(x => x.Context.Message.EventType == EventTypes.CampaignGoalReached)
            .ToList();

        Assert.Equal(1025m, campaign.TotalRaised);
        Assert.Equal("Processed", donation.Status);
        Assert.Single(processedEvents);
        Assert.Single(goalEvents);
        Assert.Equal("corr-donation-first", processedEvents[0].Context.Message.CorrelationId);
        Assert.Equal(first.EventId.ToString(), processedEvents[0].Context.Message.CausationId);
    }

    [Fact]
    public async Task Payment_from_another_tenant_cannot_change_campaign_or_donation()
    {
        await using var host = await CreateHostAsync();
        var (campaignId, donationId) = await SeedDonationAsync(host, 300m, 50m);
        var consumer = host.Harness.GetConsumerHarness<PaymentConfirmedConsumer>();
        var message = CreateEvent(
            CreatePayment(campaignId, donationId, 50m, "outro-tenant"),
            "corr-cross-tenant");

        await host.Harness.Bus.Publish(message);

        Assert.True(await consumer.Consumed.Any<IntegrationEvent>(x => x.Context.Message.EventId == message.EventId));

        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CampaignsDbContext>();
        var campaign = await db.Campaigns.SingleAsync(x => x.Id == campaignId);
        var donation = await db.Donations.SingleAsync(x => x.Id == donationId);

        Assert.Equal(300m, campaign.TotalRaised);
        Assert.Equal("Pending", donation.Status);
        Assert.DoesNotContain(
            host.Harness.Published.Select<IntegrationEvent>(),
            x => x.Context.Message.EventType == EventTypes.DonationProcessed);
    }

    private static async Task<MassTransitComponentHost> CreateHostAsync()
    {
        var host = await MassTransitComponentHost.CreateAsync(
            (services, connection) =>
                services.AddDbContext<CampaignsDbContext>(options => options.UseSqlite(connection)),
            bus => bus.AddConsumer<PaymentConfirmedConsumer>());

        await using var scope = host.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<CampaignsDbContext>().Database.EnsureCreatedAsync();
        return host;
    }

    private static async Task<(Guid CampaignId, Guid DonationId)> SeedDonationAsync(
        MassTransitComponentHost host,
        decimal totalRaised,
        decimal amount)
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
        var donation = new Donation
        {
            CampaignId = campaign.Id,
            TenantId = campaign.TenantId,
            DonorId = "donor-component",
            DonorEmail = "doador@example.org",
            Amount = amount,
            PaymentMethod = "pix",
            Status = "Pending"
        };

        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CampaignsDbContext>();
        db.AddRange(campaign, donation);
        await db.SaveChangesAsync();
        return (campaign.Id, donation.Id);
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

    private static IntegrationEvent CreateEvent(PaymentConfirmedPayload payload, string correlationId) =>
        IntegrationEvent.Create(
            EventTypes.PaymentConfirmed,
            payload.TenantId,
            correlationId,
            "component-tests",
            JsonSerializer.Serialize(payload, JsonOptions));
}
