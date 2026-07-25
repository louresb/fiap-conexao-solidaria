using ConexaoSolidaria.Contracts.Campaigns;

namespace ConexaoSolidaria.Contracts.Validation;

public static class CampaignRules
{
    public static IReadOnlyList<string> Validate(
        string title,
        string description,
        DateOnly startDate,
        DateOnly endDate,
        decimal goalAmount,
        DateOnly? today = null)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(title))
        {
            errors.Add("Titulo da campanha e obrigatorio.");
        }

        if (title.Trim().Length > 160)
        {
            errors.Add("Titulo da campanha deve ter no maximo 160 caracteres.");
        }

        if (string.IsNullOrWhiteSpace(description))
        {
            errors.Add("Descricao da campanha e obrigatoria.");
        }

        if (endDate < (today ?? DateOnly.FromDateTime(DateTime.UtcNow.Date)))
        {
            errors.Add("Data fim nao pode estar no passado.");
        }

        if (endDate < startDate)
        {
            errors.Add("Data fim deve ser igual ou posterior a data de inicio.");
        }

        if (goalAmount <= 0)
        {
            errors.Add("Meta financeira deve ser maior que zero.");
        }

        return errors;
    }

    public static bool CanReceiveDonation(
        CampaignStatus status,
        DateOnly startDate,
        DateOnly endDate,
        DateOnly? today = null)
    {
        var currentDate = today ?? DateOnly.FromDateTime(DateTime.UtcNow.Date);
        return status == CampaignStatus.Ativa
            && startDate <= currentDate
            && endDate >= currentDate;
    }
}
