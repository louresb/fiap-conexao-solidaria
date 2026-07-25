using System.Text.Json;

using ConexaoSolidaria.Campaigns.Api.Search;
using ConexaoSolidaria.Campaigns.Data;
using ConexaoSolidaria.Contracts.Events;

using MassTransit;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;

namespace ConexaoSolidaria.Campaigns.Api.Consumers;

public sealed class CampaignProjectionInvalidationConsumer(
    CampaignsDbContext db,
    IDistributedCache cache,
    ICampaignSearchIndexer search,
    ILogger<CampaignProjectionInvalidationConsumer> logger) : IConsumer<IntegrationEvent>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task Consume(ConsumeContext<IntegrationEvent> context)
    {
        if (context.Message.EventType != EventTypes.DonationProcessed)
        {
            return;
        }

        var donation = JsonSerializer.Deserialize<DonationProcessedPayload>(
            context.Message.PayloadJson,
            JsonOptions) ?? throw new InvalidOperationException("DonationProcessed payload is invalid.");

        await cache.RemoveAsync(
            $"active-campaigns:{donation.TenantId}",
            context.CancellationToken);

        var campaign = await db.Campaigns
            .AsNoTracking()
            .FirstOrDefaultAsync(
                item => item.Id == donation.CampaignId && item.TenantId == donation.TenantId,
                context.CancellationToken);

        if (campaign is not null)
        {
            await search.IndexAsync(campaign, context.CancellationToken);
        }

        logger.LogInformation(
            "Campaign projections refreshed for campaign {CampaignId} in tenant {TenantId}",
            donation.CampaignId,
            donation.TenantId);
    }
}
