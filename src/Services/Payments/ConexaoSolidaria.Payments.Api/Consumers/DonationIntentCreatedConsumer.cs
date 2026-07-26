using System.Text.Json;
using ConexaoSolidaria.Contracts.Events;
using ConexaoSolidaria.Payments.Api.Data;
using ConexaoSolidaria.Payments.Api.Providers;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace ConexaoSolidaria.Payments.Api.Consumers;

public sealed class DonationIntentCreatedConsumer(
    PaymentsDbContext db,
    IFakePaymentProvider provider,
    ILogger<DonationIntentCreatedConsumer> logger) : IConsumer<IntegrationEvent>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task Consume(ConsumeContext<IntegrationEvent> context)
    {
        var message = context.Message;
        if (message.EventType != EventTypes.DonationIntentCreated)
        {
            return;
        }

        var donation = JsonSerializer.Deserialize<DonationIntentCreatedPayload>(message.PayloadJson, JsonOptions)
            ?? throw new InvalidOperationException("Donation intent payload is invalid.");

        if (await db.Payments.AnyAsync(
                payment => payment.TenantId == donation.TenantId && payment.DonationId == donation.DonationId,
                context.CancellationToken))
        {
            logger.LogInformation("Payment for donation {DonationId} already exists", donation.DonationId);
            return;
        }

        var payment = provider.Create(donation);
        db.Payments.Add(payment);

        var payload = new PaymentAwaitingConfirmationPayload(
            payment.Id,
            payment.DonationId,
            payment.CampaignId,
            payment.Amount,
            payment.Currency,
            payment.PaymentMethod,
            payment.Provider,
            payment.ProviderPaymentId,
            payment.ExpiresAtUtc,
            payment.TenantId);

        await context.Publish(
            IntegrationEvent.Create(
                EventTypes.PaymentAwaitingConfirmation,
                payment.TenantId,
                message.CorrelationId,
                "payments-api",
                JsonSerializer.Serialize(payload, JsonOptions),
                message.EventId.ToString()),
            context.CancellationToken);

        await db.SaveChangesAsync(context.CancellationToken);
        logger.LogInformation("Sandbox payment {PaymentId} created for donation {DonationId}", payment.Id, payment.DonationId);
    }
}
