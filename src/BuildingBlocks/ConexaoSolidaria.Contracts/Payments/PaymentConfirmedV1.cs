namespace ConexaoSolidaria.Contracts.Payments;

public sealed record PaymentConfirmedV1(
    Guid PaymentId,
    Guid DonationId,
    Guid CampaignId,
    string TenantId,
    decimal Amount,
    string Currency,
    string Provider,
    string ProviderPaymentId,
    DateTimeOffset ConfirmedAtUtc);