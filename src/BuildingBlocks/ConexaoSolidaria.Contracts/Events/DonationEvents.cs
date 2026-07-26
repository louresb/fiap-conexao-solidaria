namespace ConexaoSolidaria.Contracts.Events;

public sealed record DonationIntentCreatedPayload(
    Guid DonationId,
    Guid CampaignId,
    string DonorId,
    string DonorEmail,
    decimal Amount,
    string TenantId);

public sealed record DonationProcessedPayload(
    Guid DonationId,
    Guid CampaignId,
    decimal Amount,
    decimal TotalRaised,
    string TenantId);

public sealed record CampaignGoalReachedPayload(
    Guid CampaignId,
    string Title,
    decimal GoalAmount,
    decimal TotalRaised,
    string TenantId);
