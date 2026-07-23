namespace ConexaoSolidaria.Contracts.Donations;

public sealed record DonationIntentCreatedV1(
    Guid DonationId,
    Guid CampaignId,
    string TenantId,
    decimal Amount,
    string Currency,
    string PaymentMethod,
    string? DonorId);
