using ConexaoSolidaria.Contracts.Campaigns;
using ConexaoSolidaria.Contracts.Validation;

namespace ConexaoSolidaria.Tests;

public sealed class DomainValidationTests
{
    [Theory]
    [InlineData("390.533.447-05")]
    [InlineData("39053344705")]
    public void CpfValidator_accepts_valid_cpf(string cpf)
    {
        Assert.True(CpfValidator.IsValid(cpf));
    }

    [Theory]
    [InlineData("111.111.111-11")]
    [InlineData("123")]
    [InlineData("")]
    public void CpfValidator_rejects_invalid_cpf(string cpf)
    {
        Assert.False(CpfValidator.IsValid(cpf));
    }

    [Fact]
    public void CampaignRules_rejects_past_end_date()
    {
        var today = new DateOnly(2026, 7, 24);
        var errors = CampaignRules.Validate("Campanha", "Descricao", today.AddDays(-2), today.AddDays(-1), 100, today);

        Assert.Contains(errors, e => e.Contains("Data fim"));
    }

    [Fact]
    public void CampaignRules_rejects_goal_lower_or_equal_zero()
    {
        var today = new DateOnly(2026, 7, 24);
        var errors = CampaignRules.Validate("Campanha", "Descricao", today, today.AddDays(5), 0, today);

        Assert.Contains(errors, e => e.Contains("Meta financeira"));
    }

    [Theory]
    [InlineData(CampaignStatus.Ativa, true)]
    [InlineData(CampaignStatus.Concluida, false)]
    [InlineData(CampaignStatus.Cancelada, false)]
    public void CampaignRules_allows_donation_only_for_active_campaigns(CampaignStatus status, bool expected)
    {
        var today = new DateOnly(2026, 7, 24);
        Assert.Equal(expected, CampaignRules.CanReceiveDonation(status, today.AddDays(-1), today.AddDays(1), today));
    }

    [Fact]
    public void CampaignRules_rejects_donation_outside_campaign_period()
    {
        var today = new DateOnly(2026, 7, 24);

        Assert.False(CampaignRules.CanReceiveDonation(CampaignStatus.Ativa, today.AddDays(1), today.AddDays(10), today));
        Assert.False(CampaignRules.CanReceiveDonation(CampaignStatus.Ativa, today.AddDays(-10), today.AddDays(-1), today));
    }
}