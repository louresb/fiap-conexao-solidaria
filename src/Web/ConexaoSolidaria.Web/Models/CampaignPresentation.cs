using ConexaoSolidaria.Contracts.Campaigns;

namespace ConexaoSolidaria.Web.Models;

public sealed record CampaignPresentation(string Image, string Category, string ImpactLabel);

public static class CampaignPresentations
{
    private static readonly IReadOnlyDictionary<string, CampaignPresentation> ByTitle =
        new Dictionary<string, CampaignPresentation>(StringComparer.OrdinalIgnoreCase)
        {
            ["Mesa Cheia nas Férias"] = new(
                "/images/campaign-mesa-cheia-nas-ferias.jpg",
                "Segurança alimentar",
                "Famílias acompanhadas"),
            ["Cozinha Parceira"] = new(
                "/images/campaign-cozinha-parceira.jpg",
                "Segurança alimentar",
                "Refeições servidas"),
            ["Conexão para Aprender"] = new(
                "/images/campaign-conexao-para-aprender.jpg",
                "Inclusão digital",
                "Estudantes conectados"),
            ["Mangue Vivo"] = new(
                "/images/hero-mare-limpa.jpg",
                "Meio ambiente",
                "Ecossistema restaurado"),
            ["Praia Limpa, Bairro Vivo"] = new(
                "/images/campaign-praia-limpa-bairro-vivo.jpg",
                "Meio ambiente",
                "Resíduos retirados"),
            ["Escola Azul"] = new(
                "/images/campaign-escola-azul.jpg",
                "Educação ambiental",
                "Estudantes mobilizados"),
            ["Laboratório Aberto"] = new(
                "/images/campaign-laboratorio-aberto.jpg",
                "Inclusão digital",
                "Jovens em formação"),
            ["Bolsa Dados para Estudar"] = new(
                "/images/campaign-bolsa-dados-para-estudar.jpg",
                "Inclusão digital",
                "Permanência ampliada"),
            ["Primeiro Código"] = new(
                "/images/campaign-trilha-jovem-monitor.jpg",
                "Formação tecnológica",
                "Talentos desenvolvidos")
        };

    public static CampaignPresentation For(ActiveCampaignDto campaign)
    {
        if (ByTitle.TryGetValue(campaign.Title.Trim(), out var presentation))
        {
            return presentation;
        }

        var searchable = $"{campaign.TenantId} {campaign.Title} {campaign.Description}".ToLowerInvariant();
        if (searchable.Contains("mar") || searchable.Contains("mangue") || searchable.Contains("praia") || searchable.Contains("ambient"))
        {
            return new("/images/hero-mare-limpa.jpg", "Meio ambiente", "Ecossistema protegido");
        }

        if (searchable.Contains("digital") || searchable.Contains("tecnolog") || searchable.Contains("laborat") || searchable.Contains("conex"))
        {
            return new("/images/hero-futuro-em-rede.jpg", "Inclusão digital", "Autonomia ampliada");
        }

        return new("/images/hero-esperanca-solidaria.jpg", "Desenvolvimento social", "Famílias acompanhadas");
    }
}
