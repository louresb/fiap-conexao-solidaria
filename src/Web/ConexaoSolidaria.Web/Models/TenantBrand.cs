namespace ConexaoSolidaria.Web.Models;

public sealed record TenantBrand(
    string Id,
    string Name,
    string ShortName,
    string Purpose,
    string AccentClass,
    string HeroImage);

public static class TenantCatalog
{
    public static readonly IReadOnlyList<TenantBrand> All =
    [
        new(
            "esperanca-solidaria",
            "Esperança Solidária",
            "Esperança",
            "Segurança alimentar, educação e resposta comunitária com prestação de contas aberta.",
            "tenant-esperanca",
            "/images/seguranca-alimentar.jpg"),
        new(
            "mare-limpa",
            "Maré Limpa",
            "Maré Limpa",
            "Recuperação costeira e educação ambiental conduzidas com a comunidade.",
            "tenant-mare",
            "/images/mangue-vivo.jpg"),
        new(
            "futuro-em-rede",
            "Futuro em Rede",
            "Futuro em Rede",
            "Inclusão digital e formação tecnológica para ampliar autonomia e oportunidades.",
            "tenant-futuro",
            "/images/inclusao-digital.jpg")
    ];

    public static TenantBrand Resolve(string? tenantId) =>
        All.FirstOrDefault(tenant => string.Equals(tenant.Id, tenantId, StringComparison.OrdinalIgnoreCase))
        ?? All[0];
}
