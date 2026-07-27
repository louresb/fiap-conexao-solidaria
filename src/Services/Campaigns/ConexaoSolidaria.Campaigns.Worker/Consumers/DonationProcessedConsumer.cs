using System.Text.Json;

using ConexaoSolidaria.Campaigns.Data;
using ConexaoSolidaria.Contracts.Campaigns;
using ConexaoSolidaria.Contracts.Events;

using MassTransit;

using Microsoft.EntityFrameworkCore;

namespace ConexaoSolidaria.Campaigns.Worker.Consumers;

public sealed class DonationProcessedConsumer(
    CampaignsDbContext db,
    ILogger<DonationProcessedConsumer> logger) : IConsumer<IntegrationEvent>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task Consume(ConsumeContext<IntegrationEvent> context)
    {
        var message = context.Message;
        if (message.EventType != EventTypes.DonationProcessed)
        {
            return;
        }

        var donation = JsonSerializer.Deserialize<DonationProcessedPayload>(message.PayloadJson, JsonOptions)
            ?? throw new InvalidOperationException("DonationProcessed payload is invalid.");
        var campaign = await db.Campaigns.FirstOrDefaultAsync(
            item => item.Id == donation.CampaignId && item.TenantId == donation.TenantId,
            context.CancellationToken)
            ?? throw new InvalidOperationException($"Campaign {donation.CampaignId} was not found.");

        var previousTotal = campaign.TotalRaised;
        campaign.TotalRaised += donation.Amount;
        campaign.UpdatedAtUtc = DateTimeOffset.UtcNow;
        db.DonationProjections.Add(new DonationProjection
        {
            DonationId = donation.DonationId,
            CampaignId = donation.CampaignId,
            TenantId = donation.TenantId,
            Amount = donation.Amount,
            ProcessedAtUtc = message.OccurredAtUtc
        });

        if (previousTotal < campaign.GoalAmount && campaign.TotalRaised >= campaign.GoalAmount)
        {
            var goalReached = new CampaignGoalReachedPayload(
                campaign.Id,
                campaign.Title,
                campaign.GoalAmount,
                campaign.TotalRaised,
                campaign.TenantId);
            await context.Publish(
                IntegrationEvent.Create(
                    EventTypes.CampaignGoalReached,
                    campaign.TenantId,
                    message.CorrelationId,
                    "campaigns-worker",
                    JsonSerializer.Serialize(goalReached, JsonOptions),
                    message.EventId.ToString()),
                context.CancellationToken);
        }

        await db.SaveChangesAsync(context.CancellationToken);

        var projectionUpdated = new CampaignDto(
            campaign.Id,
            campaign.TenantId,
            campaign.Title,
            campaign.Description,
            campaign.StartDate,
            campaign.EndDate,
            campaign.GoalAmount,
            campaign.TotalRaised,
            campaign.Status);
        await context.Publish(
            IntegrationEvent.Create(
                EventTypes.CampaignProjectionUpdated,
                campaign.TenantId,
                message.CorrelationId,
                "campaigns-worker",
                JsonSerializer.Serialize(projectionUpdated, JsonOptions),
                message.EventId.ToString()),
            context.CancellationToken);

        logger.LogInformation(
            "Campaign {CampaignId} projection committed from donation {DonationId}. TotalRaised={TotalRaised}",
            campaign.Id,
            donation.DonationId,
            campaign.TotalRaised);
    }
}
