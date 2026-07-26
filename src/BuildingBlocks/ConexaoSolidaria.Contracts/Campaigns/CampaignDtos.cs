namespace ConexaoSolidaria.Contracts.Campaigns;

public sealed record CampaignDto(
    Guid Id,
    string TenantId,
    string Title,
    string Description,
    DateOnly StartDate,
    DateOnly EndDate,
    decimal GoalAmount,
    decimal TotalRaised,
    CampaignStatus Status);

public sealed record ActiveCampaignDto(
    Guid Id,
    string TenantId,
    string Title,
    string Description,
    decimal GoalAmount,
    decimal TotalRaised,
    decimal ProgressPercent,
    DateOnly EndDate);

public sealed record CreateCampaignRequest(
    string Title,
    string Description,
    DateOnly StartDate,
    DateOnly EndDate,
    decimal GoalAmount,
    CampaignStatus Status = CampaignStatus.Ativa,
    string? TenantId = null);

public sealed record UpdateCampaignRequest(
    string Title,
    string Description,
    DateOnly StartDate,
    DateOnly EndDate,
    decimal GoalAmount,
    CampaignStatus Status);

public sealed record DonationIntentRequest(
    Guid CampaignId,
    decimal Amount,
    string? DonorId,
    string? DonorEmail,
    string? TenantId = null);
