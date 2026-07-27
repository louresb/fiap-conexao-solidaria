namespace ConexaoSolidaria.Campaigns.Data;

public sealed class DonationProjection
{
    public Guid DonationId { get; set; }
    public Guid CampaignId { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public DateTimeOffset ProcessedAtUtc { get; set; }
}
