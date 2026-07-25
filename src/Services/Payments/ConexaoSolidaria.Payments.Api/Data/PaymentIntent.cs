namespace ConexaoSolidaria.Payments.Api.Data;

public sealed class PaymentIntent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DonationId { get; set; }
    public Guid CampaignId { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "BRL";
    public string PaymentMethod { get; set; } = "pix";
    public string Provider { get; set; } = "sandbox";
    public string ProviderPaymentId { get; set; } = string.Empty;
    public string Status { get; set; } = "WaitingConfirmation";
    public string? QrCodePayload { get; set; }
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ConfirmedAtUtc { get; set; }
}