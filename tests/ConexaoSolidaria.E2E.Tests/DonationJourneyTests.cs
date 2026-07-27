using System.Text.RegularExpressions;

using Microsoft.Playwright;
using Microsoft.Playwright.Xunit;

namespace ConexaoSolidaria.E2E.Tests;

public sealed class DonationJourneyTests : PageTest
{
    private static readonly string? BaseUrl = Environment.GetEnvironmentVariable("E2E_BASE_URL");

    public override BrowserNewContextOptions ContextOptions() => new()
    {
        BaseURL = BaseUrl ?? "http://localhost:5000",
        Locale = "pt-BR",
        ViewportSize = new ViewportSize { Width = 1440, Height = 900 }
    };

    [Fact]
    [Trait("Category", "E2E")]
    public async Task Donor_can_authenticate_and_complete_the_sandbox_donation_journey()
    {
        EnsureConfigured();

        await Page.GotoAsync("/acesso");
        await Page.GetByText("Contas para avaliação").ClickAsync();

        var donorAccess = Page.Locator(".local-access > div").Nth(1);
        var username = (await donorAccess.Locator("code").Nth(0).TextContentAsync())!.Trim();
        var password = (await donorAccess.Locator("code").Nth(1).TextContentAsync())!.Trim();

        await Page.GetByRole(AriaRole.Link, new() { Name = "Área do doador" }).ClickAsync();
        await Page.Locator("#username").FillAsync(username);
        await Page.Locator("#password").FillAsync(password);
        await Page.Locator("#kc-login").ClickAsync();

        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Sair" })).ToBeVisibleAsync();
        await Page.WaitForTimeoutAsync(1_000);
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Esperança Solidária" })).ToBeVisibleAsync();
        await Page.Locator(".campaign-card h3 a").First.ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Faça parte desta campanha" })).ToBeVisibleAsync();
        await Page.WaitForTimeoutAsync(1_000);

        var raisedBefore = await Page.Locator(".detail-progress strong").TextContentAsync();
        await Page.Locator(".amount-options button").Nth(1).ClickAsync(new() { Force = true });
        await Page.GetByRole(AriaRole.Button, new() { Name = "Continuar contribuição" }).ClickAsync();
        await Expect(Page.GetByText("Aguardando confirmação")).ToBeVisibleAsync(new() { Timeout = 15_000 });

        await Page.GetByText("Ambiente de pagamento").ClickAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Confirmar no sandbox" }).ClickAsync();
        await Expect(Page.GetByText("Contribuição confirmada.")).ToBeVisibleAsync(new() { Timeout = 15_000 });

        await Expect(Page.Locator(".detail-progress strong")).Not.ToHaveTextAsync(raisedBefore!);
        var campaignTitle = (await Page.Locator(".detail-summary h1").TextContentAsync())!.Trim();

        await Page.GetByRole(AriaRole.Link, new() { Name = "Minhas doações" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Minhas contribuições" })).ToBeVisibleAsync();
        await Expect(Page.GetByText(campaignTitle, new() { Exact = true }).First).ToBeVisibleAsync();
        await Expect(Page.GetByText("Confirmada", new() { Exact = true }).First).ToBeVisibleAsync();
    }

    [Fact]
    [Trait("Category", "E2E")]
    public async Task Public_experience_supports_campaign_discovery_and_tenant_switching()
    {
        EnsureConfigured();

        await Page.GotoAsync("/");
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Esperança Solidária" })).ToBeVisibleAsync();
        await Page.GetByLabel("Buscar campanha").FillAsync("mesa");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Buscar" }).ClickAsync();
        await Expect(Page.GetByText("Mesa Cheia nas Férias")).ToBeVisibleAsync();

        await Page.GotoAsync("/?tenant=mare-limpa");
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Maré Limpa" })).ToBeVisibleAsync();

        await Page.GotoAsync("/transparencia?tenant=esperanca-solidaria");
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Contribuições confirmadas" })).ToBeVisibleAsync();
        await Expect(Page.GetByText("Doação anônima").First).ToBeVisibleAsync();
    }

    [Fact]
    [Trait("Category", "E2E")]
    public async Task Manager_can_authenticate_and_create_a_campaign()
    {
        EnsureConfigured();

        await Page.GotoAsync("/acesso");
        await Page.Locator(".local-access summary").ClickAsync();

        var managerAccess = Page.Locator(".local-access > div").First;
        var username = (await managerAccess.Locator("code").Nth(0).TextContentAsync())!.Trim();
        var password = (await managerAccess.Locator("code").Nth(1).TextContentAsync())!.Trim();

        await Page.Locator(".access-role").Nth(1).ClickAsync();
        await Page.Locator("#username").FillAsync(username);
        await Page.Locator("#password").FillAsync(password);
        await Page.Locator("#kc-login").ClickAsync();

        await Page.GotoAsync("/gestao/campanhas/nova");
        await Expect(Page.Locator(".editor-page form")).ToBeVisibleAsync();
        await Page.WaitForTimeoutAsync(1_000);
        var campaignTitle = $"Campanha E2E {DateTime.UtcNow:MMddHHmmss}";
        await Page.Locator("#title").FillAsync(campaignTitle);
        await Page.Locator("#description").FillAsync("Campanha criada automaticamente para validar a jornada de gestao.");
        await Page.Locator("#goal").FillAsync("12000");
        await Page.Locator(".editor-actions button[type=submit]").ClickAsync();

        await Expect(Page).ToHaveURLAsync(new Regex("/gestao/campanhas$"));
        await Expect(Page.GetByText(campaignTitle, new() { Exact = true })).ToBeVisibleAsync();
    }

    [Fact]
    [Trait("Category", "E2E")]
    public async Task Platform_admin_can_authenticate_and_review_tenants_and_service_health()
    {
        EnsureConfigured();

        await Page.GotoAsync("/acesso");
        await Page.Locator(".local-access summary").ClickAsync();

        var adminAccess = Page.Locator(".local-access > div").Nth(2);
        var username = (await adminAccess.Locator("code").Nth(0).TextContentAsync())!.Trim();
        var password = (await adminAccess.Locator("code").Nth(1).TextContentAsync())!.Trim();

        await Page.Locator(".access-role").Nth(2).ClickAsync();
        await Page.Locator("#username").FillAsync(username);
        await Page.Locator("#password").FillAsync(password);
        await Page.Locator("#kc-login").ClickAsync();

        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Visão operacional da plataforma." })).ToBeVisibleAsync();
        await Expect(Page.Locator(".tenant-admin-list").GetByText("Esperança Solidária", new() { Exact = true })).ToBeVisibleAsync();
        await Expect(Page.GetByText("Disponível")).ToHaveCountAsync(6);
    }

    private static void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(BaseUrl))
        {
            throw new InvalidOperationException(
                "E2E_BASE_URL deve apontar para uma instancia ativa da plataforma.");
        }
    }
}
