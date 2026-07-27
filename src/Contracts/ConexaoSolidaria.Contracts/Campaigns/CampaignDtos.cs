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
    CampaignStatus Status = CampaignStatus.Rascunho,
    string? TenantId = null);

public sealed record UpdateCampaignRequest(
    string Title,
    string Description,
    DateOnly StartDate,
    DateOnly EndDate,
    decimal GoalAmount,
    CampaignStatus Status);

public sealed record PublicDonationDto(
    Guid DonationId,
    Guid CampaignId,
    string CampaignTitle,
    decimal Amount,
    DateTimeOffset ProcessedAtUtc);

public sealed record TransparencySnapshotDto(
    string TenantId,
    decimal TotalRaised,
    decimal PublishedGoal,
    decimal AverageProgress,
    int ActiveCampaigns,
    IReadOnlyList<PublicDonationDto> RecentDonations,
    DateTimeOffset GeneratedAtUtc);
