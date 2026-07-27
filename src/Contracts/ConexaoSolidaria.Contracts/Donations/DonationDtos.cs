namespace ConexaoSolidaria.Contracts.Donations;

public sealed record CreateDonationRequest(
    Guid CampaignId,
    decimal Amount,
    string PaymentMethod = "pix",
    string? DonorId = null,
    string? DonorEmail = null);

public sealed record DonationDto(
    Guid Id,
    Guid CampaignId,
    string CampaignTitle,
    string TenantId,
    string DonorId,
    string DonorEmail,
    decimal Amount,
    string Currency,
    string PaymentMethod,
    string Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ConfirmedAtUtc);
