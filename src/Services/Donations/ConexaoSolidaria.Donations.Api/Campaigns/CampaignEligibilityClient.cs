using System.Net;
using System.Net.Http.Json;

using ConexaoSolidaria.Contracts.Campaigns;
using ConexaoSolidaria.ServiceDefaults.Http;

namespace ConexaoSolidaria.Donations.Api.Campaigns;

public interface ICampaignEligibilityGateway
{
    Task<CampaignDto?> GetAsync(Guid campaignId, string tenantId, CancellationToken cancellationToken);
}

public sealed class CampaignEligibilityClient(HttpClient client) : ICampaignEligibilityGateway
{
    public async Task<CampaignDto?> GetAsync(
        Guid campaignId,
        string tenantId,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/public/campaigns/{campaignId}");
        request.Headers.Add(CorrelationTenantMiddleware.TenantHeader, tenantId);
        using var response = await client.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<CampaignDto>(cancellationToken);
    }
}
