using ConexaoSolidaria.Contracts.Campaigns;

namespace ConexaoSolidaria.Web.Models;

public sealed record CampaignPresentation(
    string Image,
    string Category,
    string? DescriptionResourceKey = null);

public static class CampaignPresentations
{
    private static readonly IReadOnlyDictionary<string, CampaignPresentation> ByTitle =
        new Dictionary<string, CampaignPresentation>(StringComparer.OrdinalIgnoreCase)
        {
            ["Mesa Cheia nas Férias"] = new(
                "/images/campaign-mesa-cheia-nas-ferias.jpg",
                "Segurança alimentar",
                "CampaignDescription.MesaCheiaNasFerias"),
            ["Cozinha Parceira"] = new(
                "/images/campaign-cozinha-parceira.jpg",
                "Segurança alimentar",
                "CampaignDescription.CozinhaParceira"),
            ["Conexão para Aprender"] = new(
                "/images/campaign-conexao-para-aprender.jpg",
                "Inclusão digital",
                "CampaignDescription.ConexaoParaAprender"),
            ["Mangue Vivo"] = new(
                "/images/hero-mare-limpa.jpg",
                "Meio ambiente",
                "CampaignDescription.MangueVivo"),
            ["Praia Limpa, Bairro Vivo"] = new(
                "/images/campaign-praia-limpa-bairro-vivo.jpg",
                "Meio ambiente",
                "CampaignDescription.PraiaLimpaBairroVivo"),
            ["Escola Azul"] = new(
                "/images/campaign-escola-azul.jpg",
                "Educação ambiental",
                "CampaignDescription.EscolaAzul"),
            ["Laboratório Aberto"] = new(
                "/images/campaign-laboratorio-aberto.jpg",
                "Inclusão digital",
                "CampaignDescription.LaboratorioAberto"),
            ["Bolsa Dados para Estudar"] = new(
                "/images/campaign-bolsa-dados-para-estudar.jpg",
                "Inclusão digital",
                "CampaignDescription.BolsaDadosParaEstudar"),
            ["Primeiro Código"] = new(
                "/images/campaign-trilha-jovem-monitor.jpg",
                "Formação tecnológica",
                "CampaignDescription.PrimeiroCodigo")
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
            return new("/images/hero-mare-limpa.jpg", "Meio ambiente");
        }

        if (searchable.Contains("digital") || searchable.Contains("tecnolog") || searchable.Contains("laborat") || searchable.Contains("conex"))
        {
            return new("/images/hero-futuro-em-rede.jpg", "Inclusão digital");
        }

        return new("/images/hero-esperanca-solidaria.jpg", "Desenvolvimento social");
    }
}
