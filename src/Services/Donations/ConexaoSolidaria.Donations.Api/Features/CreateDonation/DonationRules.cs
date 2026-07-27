namespace ConexaoSolidaria.Donations.Api.Features.CreateDonation;

public static class DonationRules
{
    private static readonly HashSet<string> SupportedPaymentMethods =
        new(StringComparer.OrdinalIgnoreCase) { "pix", "credit_card", "bank_slip" };

    public static Dictionary<string, string[]> Validate(decimal amount, string? paymentMethod)
    {
        var errors = new Dictionary<string, string[]>();
        if (amount <= 0)
        {
            errors["amount"] = ["Valor da doacao deve ser maior que zero."];
        }

        if (string.IsNullOrWhiteSpace(paymentMethod) || !SupportedPaymentMethods.Contains(paymentMethod.Trim()))
        {
            errors["paymentMethod"] = ["Use pix, credit_card ou bank_slip."];
        }

        return errors;
    }
}
