using System.Text.Json;

using ConexaoSolidaria.Contracts.Events;
using ConexaoSolidaria.Payments.Api.Data;

using MassTransit;

using Microsoft.EntityFrameworkCore;

namespace ConexaoSolidaria.Payments.Api.Services;

public sealed class PaymentConfirmationService(
    PaymentsDbContext db,
    IPublishEndpoint publishEndpoint,
    ILogger<PaymentConfirmationService> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<PaymentConfirmationResult> ConfirmAsync(
        PaymentIntent payment,
        string providerEventId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        if (await db.Webhooks.AnyAsync(webhook => webhook.ProviderEventId == providerEventId, cancellationToken))
        {
            return PaymentConfirmationResult.AlreadyProcessed;
        }

        db.Webhooks.Add(new PaymentWebhook
        {
            ProviderEventId = providerEventId,
            PaymentId = payment.Id,
            Status = "approved"
        });

        if (payment.Status == "Confirmed")
        {
            await db.SaveChangesAsync(cancellationToken);
            return PaymentConfirmationResult.AlreadyProcessed;
        }

        payment.Status = "Confirmed";
        payment.ConfirmedAtUtc = DateTimeOffset.UtcNow;

        var payload = new PaymentConfirmedPayload(
            payment.Id,
            payment.DonationId,
            payment.CampaignId,
            payment.Amount,
            payment.Currency,
            payment.Provider,
            payment.ProviderPaymentId,
            payment.ConfirmedAtUtc.Value,
            payment.TenantId);

        await publishEndpoint.Publish(
            IntegrationEvent.Create(
                EventTypes.PaymentConfirmed,
                payment.TenantId,
                correlationId,
                "payments-api",
                JsonSerializer.Serialize(payload, JsonOptions),
                providerEventId),
            cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Payment {PaymentId} confirmed by provider event {ProviderEventId}", payment.Id, providerEventId);
        return PaymentConfirmationResult.Confirmed;
    }
}

public enum PaymentConfirmationResult
{
    Confirmed,
    AlreadyProcessed
}