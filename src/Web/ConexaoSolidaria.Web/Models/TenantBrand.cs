namespace ConexaoSolidaria.Web.Models;

public sealed record TenantBrand(
    string Id,
    string Name,
    string ShortName,
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
            "tenant-esperanca",
            "/images/hero-esperanca-solidaria.jpg"),
        new(
            "mare-limpa",
            "Maré Limpa",
            "Maré Limpa",
            "tenant-mare",
            "/images/hero-mare-limpa.jpg"),
        new(
            "futuro-em-rede",
            "Futuro em Rede",
            "Futuro em Rede",
            "tenant-futuro",
            "/images/hero-futuro-em-rede.jpg")
    ];

    public static TenantBrand Resolve(string? tenantId) =>
        All.FirstOrDefault(tenant => string.Equals(tenant.Id, tenantId, StringComparison.OrdinalIgnoreCase))
        ?? All[0];
}
