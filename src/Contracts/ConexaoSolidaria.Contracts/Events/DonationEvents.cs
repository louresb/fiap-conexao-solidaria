namespace ConexaoSolidaria.Contracts.Events;

public sealed record DonationIntentCreatedPayload(
    Guid DonationId,
    Guid CampaignId,
    string DonorId,
    string DonorEmail,
    decimal Amount,
    string TenantId,
    string PaymentMethod);

public sealed record PaymentAwaitingConfirmationPayload(
    Guid PaymentId,
    Guid DonationId,
    Guid CampaignId,
    decimal Amount,
    string Currency,
    string PaymentMethod,
    string Provider,
    string ProviderPaymentId,
    DateTimeOffset ExpiresAtUtc,
    string TenantId);

public sealed record PaymentConfirmedPayload(
    Guid PaymentId,
    Guid DonationId,
    Guid CampaignId,
    decimal Amount,
    string Currency,
    string Provider,
    string ProviderPaymentId,
    DateTimeOffset ConfirmedAtUtc,
    string TenantId);

public sealed record DonationProcessedPayload(
    Guid DonationId,
    Guid CampaignId,
    decimal Amount,
    string TenantId);

public sealed record CampaignGoalReachedPayload(
    Guid CampaignId,
    string Title,
    decimal GoalAmount,
    decimal TotalRaised,
    string TenantId);
