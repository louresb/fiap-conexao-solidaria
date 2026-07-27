using System.Security.Cryptography;
using System.Text;

using ConexaoSolidaria.Contracts.Events;
using ConexaoSolidaria.Payments.Api.Data;

using Microsoft.Extensions.Options;

namespace ConexaoSolidaria.Payments.Api.Providers;

public sealed class FakePaymentOptions
{
    public string ProviderName { get; set; } = "conexao-sandbox";
    public string WebhookSecret { get; set; } = string.Empty;
    public int ExpirationMinutes { get; set; } = 15;
}

public interface IFakePaymentProvider
{
    PaymentIntent Create(DonationIntentCreatedPayload donation);
    bool IsValidSignature(string payload, string signature);
}

public sealed class FakePaymentProvider(IOptions<FakePaymentOptions> options) : IFakePaymentProvider
{
    private readonly FakePaymentOptions _options = options.Value;

    public PaymentIntent Create(DonationIntentCreatedPayload donation)
    {
        var paymentId = Guid.NewGuid();
        var providerPaymentId = $"sandbox-{paymentId:N}";

        return new PaymentIntent
        {
            Id = paymentId,
            DonationId = donation.DonationId,
            CampaignId = donation.CampaignId,
            TenantId = donation.TenantId,
            Amount = donation.Amount,
            PaymentMethod = donation.PaymentMethod,
            Provider = _options.ProviderName,
            ProviderPaymentId = providerPaymentId,
            QrCodePayload = donation.PaymentMethod == "pix"
                ? $"PIX-SANDBOX|{providerPaymentId}|{donation.Amount:0.00}|BRL"
                : null,
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(Math.Clamp(_options.ExpirationMinutes, 5, 60))
        };
    }

    public bool IsValidSignature(string payload, string signature)
    {
        if (string.IsNullOrWhiteSpace(_options.WebhookSecret) || string.IsNullOrWhiteSpace(signature))
        {
            return false;
        }

        var expected = HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(_options.WebhookSecret),
            Encoding.UTF8.GetBytes(payload));

        return TryDecodeHex(signature, out var received)
            && CryptographicOperations.FixedTimeEquals(expected, received);
    }

    private static bool TryDecodeHex(string value, out byte[] bytes)
    {
        try
        {
            bytes = Convert.FromHexString(value);
            return true;
        }
        catch (FormatException)
        {
            bytes = [];
            return false;
        }
    }
}
