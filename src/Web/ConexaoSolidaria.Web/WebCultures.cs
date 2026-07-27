using System.Globalization;

namespace ConexaoSolidaria.Web;

public static class WebCultures
{
    public static readonly CultureInfo Default = CultureInfo.GetCultureInfo("pt-BR");

    public static readonly IReadOnlyList<CultureInfo> Supported =
    [
        Default,
        CultureInfo.GetCultureInfo("en-US")
    ];

    public static CultureInfo Resolve(string? culture) =>
        Supported.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, culture, StringComparison.OrdinalIgnoreCase))
        ?? Default;
}
