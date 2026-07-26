using ConexaoSolidaria.Contracts.Campaigns;

namespace ConexaoSolidaria.Campaigns.Data;

public sealed class Campaign
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string TenantId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public decimal GoalAmount { get; set; }
    public decimal TotalRaised { get; set; }
    public CampaignStatus Status { get; set; } = CampaignStatus.Ativa;
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; set; }

    public List<Donation> Donations { get; set; } = [];
}
