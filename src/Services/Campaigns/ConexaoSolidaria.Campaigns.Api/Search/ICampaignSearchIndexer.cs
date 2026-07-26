using ConexaoSolidaria.Campaigns.Data;
using ConexaoSolidaria.Contracts.Campaigns;

namespace ConexaoSolidaria.Campaigns.Api.Search;

public interface ICampaignSearchIndexer
{
    Task IndexAsync(Campaign campaign, CancellationToken cancellationToken);
    Task<IReadOnlyList<ActiveCampaignDto>> SearchAsync(string tenantId, string query, int limit, CancellationToken cancellationToken);
}
