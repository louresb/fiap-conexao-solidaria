using System.Text.Json;

using ConexaoSolidaria.Contracts.Events;
using ConexaoSolidaria.Donations.Api.Data;
using ConexaoSolidaria.Donations.Api.Observability;

using MassTransit;

using Microsoft.EntityFrameworkCore;

namespace ConexaoSolidaria.Donations.Api.Consumers;

public sealed class PaymentConfirmedConsumer(
    DonationsDbContext db,
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
        var donation = await db.Donations.FirstOrDefaultAsync(
            item => item.Id == payment.DonationId && item.TenantId == payment.TenantId,
            context.CancellationToken)
            ?? throw new InvalidOperationException($"Donation {payment.DonationId} was not found.");

        if (donation.Status == DonationStatus.Confirmed)
        {
            DonationMetrics.Processed.WithLabels(payment.TenantId, "duplicate").Inc();
            logger.LogInformation("Donation {DonationId} was already confirmed", donation.Id);
            return;
        }

        if (donation.Amount != payment.Amount || donation.CampaignId != payment.CampaignId)
        {
            donation.Status = DonationStatus.Rejected;
            await db.SaveChangesAsync(context.CancellationToken);
            DonationMetrics.Processed.WithLabels(payment.TenantId, "rejected").Inc();
            logger.LogWarning("Payment {PaymentId} does not match donation {DonationId}", payment.PaymentId, donation.Id);
            return;
        }

        donation.Status = DonationStatus.Confirmed;
        donation.ConfirmedAtUtc = payment.ConfirmedAtUtc;

        var payload = new DonationProcessedPayload(
            donation.Id,
            donation.CampaignId,
            donation.Amount,
            donation.TenantId);
        await context.Publish(
            IntegrationEvent.Create(
                EventTypes.DonationProcessed,
                donation.TenantId,
                message.CorrelationId,
                "donations-api",
                JsonSerializer.Serialize(payload, JsonOptions),
                message.EventId.ToString()),
            context.CancellationToken);
        await db.SaveChangesAsync(context.CancellationToken);

        DonationMetrics.Processed.WithLabels(payment.TenantId, "confirmed").Inc();
        DonationMetrics.ProcessingLatency
            .WithLabels(payment.TenantId)
            .Observe(Math.Max(0, (DateTimeOffset.UtcNow - message.OccurredAtUtc).TotalSeconds));

        logger.LogInformation("Donation {DonationId} confirmed after payment {PaymentId}", donation.Id, payment.PaymentId);
    }
}
