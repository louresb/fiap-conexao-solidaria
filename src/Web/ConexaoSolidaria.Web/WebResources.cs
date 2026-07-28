using System.Globalization;
using System.Resources;

namespace ConexaoSolidaria.Web;

public sealed class WebResources;

public static class ValidationMessages
{
    private static readonly ResourceManager Resources = new(
        "ConexaoSolidaria.Web.Resources.WebResources",
        typeof(WebResources).Assembly);

    public static string FullNameRequired => Get("Validation.FullNameRequired");
    public static string FullNameInvalid => Get("Validation.FullNameInvalid");
    public static string EmailRequired => Get("Validation.EmailRequired");
    public static string EmailInvalid => Get("Validation.EmailInvalid");
    public static string CpfRequired => Get("Validation.CpfRequired");
    public static string CpfInvalid => Get("Validation.CpfInvalid");
    public static string PasswordRequired => Get("Validation.PasswordRequired");
    public static string PasswordLength => Get("Validation.PasswordLength");
    public static string ConsentRequired => Get("Validation.ConsentRequired");
    public static string CampaignTitleRequired => Get("Validation.CampaignTitleRequired");
    public static string CampaignTitleLength => Get("Validation.CampaignTitleLength");
    public static string CampaignDescriptionRequired => Get("Validation.CampaignDescriptionRequired");
    public static string CampaignDescriptionLength => Get("Validation.CampaignDescriptionLength");
    public static string CampaignGoalPositive => Get("Validation.CampaignGoalPositive");

    private static string Get(string name) =>
        Resources.GetString(name, CultureInfo.CurrentUICulture) ?? name;
}
