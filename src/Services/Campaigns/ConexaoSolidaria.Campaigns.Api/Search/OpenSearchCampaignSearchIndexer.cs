using System.Net.Http.Json;
using System.Text.Json;

using ConexaoSolidaria.Campaigns.Data;
using ConexaoSolidaria.Contracts.Campaigns;

using Microsoft.Extensions.Options;

namespace ConexaoSolidaria.Campaigns.Api.Search;

public sealed class OpenSearchOptions
{
    public bool Enabled { get; set; }
    public string BaseUrl { get; set; } = "http://localhost:9200";
    public string Index { get; set; } = "campaigns";
}

public sealed class OpenSearchCampaignSearchIndexer : ICampaignSearchIndexer
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly OpenSearchOptions _options;
    private readonly ILogger<OpenSearchCampaignSearchIndexer> _logger;

    public OpenSearchCampaignSearchIndexer(
        HttpClient httpClient,
        IOptions<OpenSearchOptions> options,
        ILogger<OpenSearchCampaignSearchIndexer> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task IndexAsync(Campaign campaign, CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            return;
        }

        try
        {
            var document = ToDto(campaign);
            var response = await _httpClient.PutAsJsonAsync(
                $"{_options.BaseUrl}/{_options.Index}/_doc/{campaign.Id}",
                document,
                JsonOptions,
                cancellationToken);
            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OpenSearch index operation skipped");
        }
    }

    public async Task<IReadOnlyList<ActiveCampaignDto>> SearchAsync(string tenantId, string query, int limit, CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            return [];
        }

        try
        {
            var searchBody = new
            {
                size = limit,
                query = new
                {
                    @bool = new
                    {
                        filter = new object[]
                        {
                            new { term = new Dictionary<string, object> { ["tenantId.keyword"] = tenantId } },
                            new { term = new Dictionary<string, object> { ["status.keyword"] = CampaignStatus.Ativa.ToString() } }
                        },
                        must = new object[]
                        {
                            new
                            {
                                multi_match = new
                                {
                                    query,
                                    fields = new[] { "title^3", "description" },
                                    fuzziness = "AUTO"
                                }
                            }
                        }
                    }
                }
            };

            var response = await _httpClient.PostAsJsonAsync(
                $"{_options.BaseUrl}/{_options.Index}/_search",
                searchBody,
                JsonOptions,
                cancellationToken);
            response.EnsureSuccessStatusCode();

            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var hits = json.RootElement.GetProperty("hits").GetProperty("hits");
            var results = new List<ActiveCampaignDto>();

            foreach (var hit in hits.EnumerateArray())
            {
                var source = hit.GetProperty("_source");
                var dto = source.Deserialize<ActiveCampaignDto>(JsonOptions);
                if (dto is not null)
                {
                    results.Add(dto);
                }
            }

            return results;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OpenSearch search operation skipped");
            return [];
        }
    }

    private static CampaignSearchDocument ToDto(Campaign campaign)
    {
        var progress = campaign.GoalAmount <= 0 ? 0 : Math.Round(campaign.TotalRaised / campaign.GoalAmount * 100, 2);
        return new CampaignSearchDocument(
            campaign.Id,
            campaign.TenantId,
            campaign.Title,
            campaign.Description,
            campaign.GoalAmount,
            campaign.TotalRaised,
            progress,
            campaign.EndDate,
            campaign.Status.ToString());
    }

    private sealed record CampaignSearchDocument(
        Guid Id,
        string TenantId,
        string Title,
        string Description,
        decimal GoalAmount,
        decimal TotalRaised,
        decimal ProgressPercent,
        DateOnly EndDate,
        string Status);
}