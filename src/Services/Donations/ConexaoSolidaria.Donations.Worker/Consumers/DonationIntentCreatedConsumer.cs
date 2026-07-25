using System.Text.Json;
using ConexaoSolidaria.Campaigns.Data;
using ConexaoSolidaria.Contracts.Campaigns;
using ConexaoSolidaria.Contracts.Events;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;

namespace ConexaoSolidaria.Donations.Worker.Consumers;

public sealed class DonationIntentCreatedConsumer : IConsumer<IntegrationEvent>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly CampaignsDbContext _db;
    private readonly IDistributedCache _cache;
    private readonly ILogger<DonationIntentCreatedConsumer> _logger;

    public DonationIntentCreatedConsumer(
        CampaignsDbContext db,
        IDistributedCache cache,
        ILogger<DonationIntentCreatedConsumer> logger)
    {
        _db = db;
        _cache = cache;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<IntegrationEvent> context)
    {
        var message = context.Message;
        if (message.EventType != EventTypes.DonationIntentCreated)
        {
            return;
        }

        var payload = JsonSerializer.Deserialize<DonationIntentCreatedPayload>(message.PayloadJson, JsonOptions)
            ?? throw new InvalidOperationException("DonationIntentCreated payload is invalid.");

        var campaign = await _db.Campaigns
            .FirstOrDefaultAsync(c => c.Id == payload.CampaignId && c.TenantId == payload.TenantId, context.CancellationToken);

        var donation = await _db.Donations
            .FirstOrDefaultAsync(d => d.Id == payload.DonationId && d.TenantId == payload.TenantId, context.CancellationToken);

        if (campaign is null || donation is null)
        {
            _logger.LogWarning("Donation {DonationId} ignored because campaign or donation was not found", payload.DonationId);
            return;
        }

        if (campaign.Status != CampaignStatus.Ativa)
        {
            donation.Status = "Rejected";
            donation.ProcessedAtUtc = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(context.CancellationToken);
            return;
        }

        donation.Status = "Processed";
        donation.ProcessedAtUtc = DateTimeOffset.UtcNow;
        campaign.TotalRaised += donation.Amount;
        campaign.UpdatedAtUtc = DateTimeOffset.UtcNow;

        var processedPayload = new DonationProcessedPayload(
            donation.Id,
            campaign.Id,
            donation.Amount,
            campaign.TotalRaised,
            payload.TenantId);

        await context.Publish(
            IntegrationEvent.Create(
                EventTypes.DonationProcessed,
                payload.TenantId,
                message.CorrelationId,
                "donations-worker",
                JsonSerializer.Serialize(processedPayload, JsonOptions),
                message.EventId.ToString()),
            context.CancellationToken);

        if (campaign.TotalRaised >= campaign.GoalAmount)
        {
            var goalPayload = new CampaignGoalReachedPayload(
                campaign.Id,
                campaign.Title,
                campaign.GoalAmount,
                campaign.TotalRaised,
                payload.TenantId);

            await context.Publish(
                IntegrationEvent.Create(
                    EventTypes.CampaignGoalReached,
                    payload.TenantId,
                    message.CorrelationId,
                    "donations-worker",
                    JsonSerializer.Serialize(goalPayload, JsonOptions),
                    message.EventId.ToString()),
                context.CancellationToken);
        }

        await _db.SaveChangesAsync(context.CancellationToken);
        await _cache.RemoveAsync($"active-campaigns:{payload.TenantId}", context.CancellationToken);

        _logger.LogInformation(
            "Donation {DonationId} processed for campaign {CampaignId}. TotalRaised={TotalRaised}",
            donation.Id,
            campaign.Id,
            campaign.TotalRaised);
    }
}
