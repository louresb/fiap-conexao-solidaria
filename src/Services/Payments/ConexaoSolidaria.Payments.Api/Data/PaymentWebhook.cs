namespace ConexaoSolidaria.Payments.Api.Data;

public sealed class PaymentWebhook
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string ProviderEventId { get; set; } = string.Empty;
    public Guid PaymentId { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset ReceivedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}