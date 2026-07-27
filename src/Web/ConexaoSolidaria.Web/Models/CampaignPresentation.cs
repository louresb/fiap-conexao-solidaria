using ConexaoSolidaria.Contracts.Campaigns;

namespace ConexaoSolidaria.Web.Models;

public sealed record CampaignPresentation(string Image, string Category, string ImpactLabel);

public static class CampaignPresentations
{
    public static CampaignPresentation For(ActiveCampaignDto campaign)
    {
        var searchable = $"{campaign.TenantId} {campaign.Title} {campaign.Description}".ToLowerInvariant();
        if (searchable.Contains("mar") || searchable.Contains("mangue") || searchable.Contains("praia") || searchable.Contains("ambient"))
        {
            return new("/images/mangue-vivo.jpg", "Meio ambiente", "Ecossistema protegido");
        }

        if (searchable.Contains("digital") || searchable.Contains("tecnolog") || searchable.Contains("laborat") || searchable.Contains("conex"))
        {
            return new("/images/inclusao-digital.jpg", "Inclusão digital", "Autonomia ampliada");
        }

        return new("/images/seguranca-alimentar.jpg", "Desenvolvimento social", "Famílias acompanhadas");
    }
}
