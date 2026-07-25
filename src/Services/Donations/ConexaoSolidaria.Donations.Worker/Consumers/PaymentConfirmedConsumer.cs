using System.Text.Json;

using ConexaoSolidaria.Campaigns.Data;
using ConexaoSolidaria.Contracts.Campaigns;
using ConexaoSolidaria.Contracts.Events;
using ConexaoSolidaria.Donations.Worker.Observability;

using MassTransit;

using Microsoft.EntityFrameworkCore;

namespace ConexaoSolidaria.Donations.Worker.Consumers;

public sealed class PaymentConfirmedConsumer(
    CampaignsDbContext db,
    ILogger<PaymentConfirmedConsumer> logger) : IConsumer<IntegrationEvent>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task Consume(ConsumeContext<IntegrationEvent> context)
    {
        var message = context.Message;
        if (message.EventType != EventTypes.PaymentConfirmed)
        {
            return;
        }

        var payment = JsonSerializer.Deserialize<PaymentConfirmedPayload>(message.PayloadJson, JsonOptions)
            ?? throw new InvalidOperationException("PaymentConfirmed payload is invalid.");

        var campaign = await db.Campaigns.FirstOrDefaultAsync(
            item => item.Id == payment.CampaignId && item.TenantId == payment.TenantId,
            context.CancellationToken);
        var donation = await db.Donations.FirstOrDefaultAsync(
            item => item.Id == payment.DonationId && item.TenantId == payment.TenantId,
            context.CancellationToken);

        if (campaign is null || donation is null)
        {
            throw new InvalidOperationException($"Donation {payment.DonationId} or campaign {payment.CampaignId} was not found.");
        }

        if (donation.Status == "Processed")
        {
            DonationMetrics.Processed.WithLabels(payment.TenantId, "duplicate").Inc();
            logger.LogInformation("Donation {DonationId} was already processed", donation.Id);
            return;
        }

        if (campaign.Status != CampaignStatus.Ativa || donation.Amount != payment.Amount)
        {
            donation.Status = "Rejected";
            donation.ProcessedAtUtc = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(context.CancellationToken);
            DonationMetrics.Processed.WithLabels(payment.TenantId, "rejected").Inc();
            logger.LogWarning("Confirmed payment {PaymentId} rejected due to campaign state or amount mismatch", payment.PaymentId);
            return;
        }

        donation.Status = "Processed";
        donation.ProcessedAtUtc = payment.ConfirmedAtUtc;
        campaign.TotalRaised += payment.Amount;
        campaign.UpdatedAtUtc = DateTimeOffset.UtcNow;

        var processedPayload = new DonationProcessedPayload(
            donation.Id,
            campaign.Id,
            donation.Amount,
            campaign.TotalRaised,
            payment.TenantId);

        await context.Publish(
            IntegrationEvent.Create(
                EventTypes.DonationProcessed,
                payment.TenantId,
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
                payment.TenantId);

            await context.Publish(
                IntegrationEvent.Create(
                    EventTypes.CampaignGoalReached,
                    payment.TenantId,
                    message.CorrelationId,
                    "donations-worker",
                    JsonSerializer.Serialize(goalPayload, JsonOptions),
                    message.EventId.ToString()),
                context.CancellationToken);
        }

        await db.SaveChangesAsync(context.CancellationToken);
        DonationMetrics.Processed.WithLabels(payment.TenantId, "processed").Inc();
        DonationMetrics.ProcessingLatency
            .WithLabels(payment.TenantId)
            .Observe(Math.Max(0, (DateTimeOffset.UtcNow - message.OccurredAtUtc).TotalSeconds));

        logger.LogInformation(
            "Donation {DonationId} processed for campaign {CampaignId}. TotalRaised={TotalRaised}",
            donation.Id,
            campaign.Id,
            campaign.TotalRaised);
    }
}