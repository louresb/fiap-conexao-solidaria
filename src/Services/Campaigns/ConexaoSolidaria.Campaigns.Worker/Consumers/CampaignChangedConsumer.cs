using System.Text.Json;

using ConexaoSolidaria.Campaigns.Data;
using ConexaoSolidaria.Campaigns.Infrastructure.Search;
using ConexaoSolidaria.Contracts.Campaigns;
using ConexaoSolidaria.Contracts.Events;

using MassTransit;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;

namespace ConexaoSolidaria.Campaigns.Worker.Consumers;

public sealed class CampaignChangedConsumer(
    CampaignsDbContext db,
    ICampaignSearchIndexer search,
    IDistributedCache cache,
    ILogger<CampaignChangedConsumer> logger) : IConsumer<IntegrationEvent>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly HashSet<string> SupportedEvents =
    [
        EventTypes.CampaignCreated,
        EventTypes.CampaignUpdated,
        EventTypes.CampaignPublished,
        EventTypes.CampaignCancelled,
        EventTypes.CampaignProjectionUpdated
    ];

    public async Task Consume(ConsumeContext<IntegrationEvent> context)
    {
        var message = context.Message;
        if (!SupportedEvents.Contains(message.EventType))
        {
            return;
        }

        var payload = JsonSerializer.Deserialize<CampaignDto>(message.PayloadJson, JsonOptions)
            ?? throw new InvalidOperationException($"{message.EventType} payload is invalid.");
        var campaign = await db.Campaigns
            .AsNoTracking()
            .FirstOrDefaultAsync(
                item => item.Id == payload.Id && item.TenantId == message.TenantId,
                context.CancellationToken);

        if (campaign is null)
        {
            logger.LogWarning(
                "Campaign {CampaignId} from {EventType} was not found for tenant {TenantId}",
                payload.Id,
                message.EventType,
                message.TenantId);
            return;
        }

        await cache.RemoveAsync($"active-campaigns:{campaign.TenantId}", context.CancellationToken);
        await search.IndexAsync(campaign, context.CancellationToken);

        logger.LogInformation(
            "Campaign {CampaignId} read models refreshed from {EventType}",
            campaign.Id,
            message.EventType);
    }
}
