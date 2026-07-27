namespace ConexaoSolidaria.Donations.Api.Data;

public static class DonationStatus
{
    public const string PendingPayment = "PendingPayment";
    public const string Confirmed = "Confirmed";
    public const string Rejected = "Rejected";
}

public sealed class Donation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CampaignId { get; set; }
    public string CampaignTitle { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public string DonorId { get; set; } = string.Empty;
    public string DonorEmail { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "BRL";
    public string PaymentMethod { get; set; } = "pix";
    public string Status { get; set; } = DonationStatus.PendingPayment;
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ConfirmedAtUtc { get; set; }
}
