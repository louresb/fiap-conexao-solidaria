using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;

using ConexaoSolidaria.Contracts.Audit;
using ConexaoSolidaria.Contracts.Auth;
using ConexaoSolidaria.Contracts.Campaigns;
using ConexaoSolidaria.Contracts.Identity;
using ConexaoSolidaria.Contracts.Knowledge;
using ConexaoSolidaria.Contracts.Payments;

using Microsoft.AspNetCore.Components.Authorization;

namespace ConexaoSolidaria.Web.Services;

public sealed class SolidariaApiClient(
    HttpClient httpClient,
    AuthenticationStateProvider authenticationStateProvider)
{
    public async Task<DonorProfileDto> RegisterDonorAsync(
        string tenantId,
        DonorRegistrationRequest body,
        CancellationToken cancellationToken)
    {
        using var request = await CreateRequestAsync(HttpMethod.Post, "/api/donors/register", tenantId);
        request.Content = JsonContent.Create(body with { TenantId = null });
        using var response = await httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<DonorProfileDto>(cancellationToken))!;
    }

    public async Task<CampaignCollection> GetActiveCampaignsAsync(string tenantId, CancellationToken cancellationToken)
    {
        using var request = await CreateRequestAsync(
            HttpMethod.Get,
            $"/api/public/campaigns?tenantId={Uri.EscapeDataString(tenantId)}",
            tenantId);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var cache = response.Headers.TryGetValues("X-Cache", out var values) ? values.FirstOrDefault() ?? "-" : "-";
        var campaigns = await response.Content.ReadFromJsonAsync<List<ActiveCampaignDto>>(cancellationToken) ?? [];
        return new CampaignCollection(campaigns, cache);
    }

    public async Task<IReadOnlyList<ActiveCampaignDto>> SearchCampaignsAsync(
        string tenantId,
        string query,
        CancellationToken cancellationToken)
    {
        using var request = await CreateRequestAsync(
            HttpMethod.Get,
            $"/api/public/campaigns/search?q={Uri.EscapeDataString(query)}&tenantId={Uri.EscapeDataString(tenantId)}&limit=12",
            tenantId);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<List<ActiveCampaignDto>>(cancellationToken) ?? [];
    }

    public async Task<IReadOnlyList<CampaignDto>> GetManagedCampaignsAsync(CancellationToken cancellationToken)
    {
        using var request = await CreateRequestAsync(HttpMethod.Get, "/api/management/campaigns", authenticatedTenantOnly: true);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<List<CampaignDto>>(cancellationToken) ?? [];
    }

    public async Task<CampaignDto> CreateCampaignAsync(CreateCampaignRequest body, CancellationToken cancellationToken)
    {
        using var request = await CreateRequestAsync(HttpMethod.Post, "/api/management/campaigns", authenticatedTenantOnly: true);
        request.Content = JsonContent.Create(body);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<CampaignDto>(cancellationToken))!;
    }

    public async Task<CampaignDto> UpdateCampaignAsync(
        Guid campaignId,
        UpdateCampaignRequest body,
        CancellationToken cancellationToken)
    {
        using var request = await CreateRequestAsync(
            HttpMethod.Put,
            $"/api/management/campaigns/{campaignId}",
            authenticatedTenantOnly: true);
        request.Content = JsonContent.Create(body);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<CampaignDto>(cancellationToken))!;
    }

    public async Task CancelCampaignAsync(Guid campaignId, CancellationToken cancellationToken)
    {
        using var request = await CreateRequestAsync(
            HttpMethod.Delete,
            $"/api/management/campaigns/{campaignId}",
            authenticatedTenantOnly: true);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task<DonationAcceptedResponse> CreateDonationAsync(
        DonationIntentRequest body,
        CancellationToken cancellationToken)
    {
        using var request = await CreateRequestAsync(HttpMethod.Post, "/api/donations", authenticatedTenantOnly: true);
        request.Content = JsonContent.Create(body);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<DonationAcceptedResponse>(cancellationToken))!;
    }

    public async Task<PaymentIntentDto?> GetPaymentByDonationAsync(Guid donationId, CancellationToken cancellationToken)
    {
        using var request = await CreateRequestAsync(
            HttpMethod.Get,
            $"/api/payments/donations/{donationId}",
            authenticatedTenantOnly: true);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<PaymentIntentDto>(cancellationToken);
    }

    public async Task ConfirmSandboxPaymentAsync(Guid paymentId, CancellationToken cancellationToken)
    {
        using var request = await CreateRequestAsync(
            HttpMethod.Post,
            $"/api/payments/{paymentId}/simulate-confirmation",
            authenticatedTenantOnly: true);
        request.Content = JsonContent.Create(new { });
        using var response = await httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task<IReadOnlyList<AuditEventDto>> GetAuditAsync(int limit, CancellationToken cancellationToken)
    {
        using var request = await CreateRequestAsync(
            HttpMethod.Get,
            $"/api/audit?limit={Math.Clamp(limit, 1, 100)}",
            authenticatedTenantOnly: true);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<List<AuditEventDto>>(cancellationToken) ?? [];
    }

    public async Task<KnowledgeAnswerDto> AskKnowledgeAsync(
        string tenantId,
        string question,
        CancellationToken cancellationToken)
    {
        using var request = await CreateRequestAsync(HttpMethod.Post, "/api/knowledge/ask", tenantId);
        request.Content = JsonContent.Create(new AskKnowledgeRequest(question));
        using var response = await httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<KnowledgeAnswerDto>(cancellationToken))!;
    }

    public async Task<UserApiContext> GetUserContextAsync()
    {
        var principal = (await authenticationStateProvider.GetAuthenticationStateAsync()).User;
        return new UserApiContext(
            principal.Identity?.IsAuthenticated == true,
            principal.FindFirstValue(AuthDefaults.TenantClaim) ?? AuthDefaults.DefaultTenantId,
            principal.FindFirstValue("sub") ?? string.Empty,
            principal.FindFirstValue("email") ?? string.Empty,
            principal.Identity?.Name ?? principal.FindFirstValue("preferred_username") ?? string.Empty,
            principal.IsInRole(AuthDefaults.ManagerRole),
            principal.IsInRole(AuthDefaults.DonorRole));
    }

    private async Task<HttpRequestMessage> CreateRequestAsync(
        HttpMethod method,
        string path,
        string? tenantId = null,
        bool authenticatedTenantOnly = false)
    {
        var principal = (await authenticationStateProvider.GetAuthenticationStateAsync()).User;
        var authenticatedTenant = principal.FindFirstValue(AuthDefaults.TenantClaim);
        var resolvedTenant = authenticatedTenantOnly
            ? authenticatedTenant ?? AuthDefaults.DefaultTenantId
            : tenantId ?? authenticatedTenant ?? AuthDefaults.DefaultTenantId;

        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("X-Tenant-Id", resolvedTenant);
        request.Headers.Add("X-Correlation-Id", Guid.NewGuid().ToString("N"));

        var accessToken = principal.FindFirstValue("access_token");
        if (!string.IsNullOrWhiteSpace(accessToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        return request;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var detail = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new SolidariaApiException(response.StatusCode, detail);
    }
}

public sealed record CampaignCollection(IReadOnlyList<ActiveCampaignDto> Items, string CacheStatus);
public sealed record DonationAcceptedResponse(Guid Id, string Status, string Message);
public sealed record UserApiContext(
    bool IsAuthenticated,
    string TenantId,
    string Subject,
    string Email,
    string DisplayName,
    bool IsManager,
    bool IsDonor);

public sealed class SolidariaApiException(HttpStatusCode statusCode, string detail)
    : Exception($"A API retornou {(int)statusCode}: {detail}")
{
    public HttpStatusCode StatusCode { get; } = statusCode;
    public string Detail { get; } = detail;
}
