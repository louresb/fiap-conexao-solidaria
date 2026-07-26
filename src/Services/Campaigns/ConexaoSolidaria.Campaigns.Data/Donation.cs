namespace ConexaoSolidaria.Campaigns.Data;

public sealed class Donation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CampaignId { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public string DonorId { get; set; } = string.Empty;
    public string DonorEmail { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string PaymentMethod { get; set; } = "pix";
    public string Status { get; set; } = "Pending";
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ProcessedAtUtc { get; set; }

    public Campaign? Campaign { get; set; }
}
