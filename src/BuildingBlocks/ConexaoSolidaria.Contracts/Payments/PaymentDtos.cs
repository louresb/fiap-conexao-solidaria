namespace ConexaoSolidaria.Contracts.Payments;

public sealed record PaymentIntentDto(
    Guid Id,
    Guid DonationId,
    Guid CampaignId,
    string TenantId,
    decimal Amount,
    string Currency,
    string PaymentMethod,
    string Provider,
    string ProviderPaymentId,
    string Status,
    string? QrCodePayload,
    DateTimeOffset ExpiresAtUtc,
    DateTimeOffset? ConfirmedAtUtc);

public sealed record FakePaymentWebhookRequest(
    string ProviderEventId,
    string ProviderPaymentId,
    string Status);