using System.Security.Cryptography;
using System.Text;
using ConexaoSolidaria.Contracts.Events;
using ConexaoSolidaria.Payments.Api.Providers;
using Microsoft.Extensions.Options;

namespace ConexaoSolidaria.Tests;

public sealed class FakePaymentProviderTests
{
    [Fact]
    public void Create_generates_pix_payload_without_exposing_a_real_charge()
    {
        var provider = CreateProvider("test-secret");
        var donation = new DonationIntentCreatedPayload(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "donor-1",
            "doador@example.org",
            125.50m,
            "esperanca-solidaria",
            "pix");

        var payment = provider.Create(donation);

        Assert.Equal("WaitingConfirmation", payment.Status);
        Assert.StartsWith("PIX-SANDBOX|", payment.QrCodePayload);
        Assert.Equal(donation.Amount, payment.Amount);
    }

    [Fact]
    public void Signature_validation_rejects_tampered_webhook()
    {
        const string secret = "test-secret";
        const string payload = "evt-1|sandbox-1|approved";
        var provider = CreateProvider(secret);
        var signature = Convert.ToHexString(HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(secret),
            Encoding.UTF8.GetBytes(payload)));

        Assert.True(provider.IsValidSignature(payload, signature));
        Assert.False(provider.IsValidSignature(payload + "-changed", signature));
    }

    private static FakePaymentProvider CreateProvider(string secret) => new(
        Options.Create(new FakePaymentOptions
        {
            WebhookSecret = secret,
            ExpirationMinutes = 15
        }));
}
