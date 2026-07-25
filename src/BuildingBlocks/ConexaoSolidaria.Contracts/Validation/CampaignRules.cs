using ConexaoSolidaria.Contracts.Campaigns;

namespace ConexaoSolidaria.Contracts.Validation;

public static class CampaignRules
{
    public static IReadOnlyList<string> Validate(string title, DateOnly endDate, decimal goalAmount)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(title))
        {
            errors.Add("Titulo da campanha e obrigatorio.");
        }

        if (endDate < DateOnly.FromDateTime(DateTime.UtcNow.Date))
        {
            errors.Add("Data fim nao pode estar no passado.");
        }

        if (goalAmount <= 0)
        {
            errors.Add("Meta financeira deve ser maior que zero.");
        }

        return errors;
    }

    public static bool CanReceiveDonation(CampaignStatus status)
    {
        return status == CampaignStatus.Ativa;
    }
}
