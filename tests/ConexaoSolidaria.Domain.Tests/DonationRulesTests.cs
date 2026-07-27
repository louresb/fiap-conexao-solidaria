using ConexaoSolidaria.Donations.Api.Features.CreateDonation;

namespace ConexaoSolidaria.Tests;

public sealed class DonationRulesTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    public void Validate_rejects_non_positive_amount(decimal amount)
    {
        var errors = DonationRules.Validate(amount, "pix");

        Assert.Contains("amount", errors);
    }

    [Theory]
    [InlineData("pix")]
    [InlineData("credit_card")]
    [InlineData("bank_slip")]
    public void Validate_accepts_supported_payment_method(string paymentMethod)
    {
        var errors = DonationRules.Validate(50m, paymentMethod);

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_rejects_unknown_payment_method()
    {
        var errors = DonationRules.Validate(50m, "crypto");

        Assert.Contains("paymentMethod", errors);
    }
}
