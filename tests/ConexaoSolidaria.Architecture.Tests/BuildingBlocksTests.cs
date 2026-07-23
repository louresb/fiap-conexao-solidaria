using ConexaoSolidaria.Contracts.Donations;
using ConexaoSolidaria.Contracts.IntegrationEvents;
using ConexaoSolidaria.SharedKernel.Primitives;

namespace ConexaoSolidaria.Architecture.Tests;

public sealed class BuildingBlocksTests
{
    [Fact]
    public void TenantId_Should_Not_Accept_Empty_Value()
    {
        Assert.Throws<ArgumentException>(() => TenantId.From(" "));
    }

    [Fact]
    public void TenantId_Should_Trim_Value()
    {
        var tenantId = TenantId.From(" tnt_esperanca ");

        Assert.Equal("tnt_esperanca", tenantId.Value);
    }

    [Fact]
    public void IntegrationEventEnvelope_Should_Carry_Tenant_And_Correlation()
    {
        var payload = new DonationIntentCreatedV1(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "tnt_esperanca",
            150m,
            "BRL",
            "pix",
            "don_001");

        var envelope = IntegrationEventEnvelope<DonationIntentCreatedV1>.Create(
            "DonationIntentCreatedV1",
            payload.TenantId,
            "corr-001",
            payload);

        Assert.Equal("DonationIntentCreatedV1", envelope.EventType);
        Assert.Equal("tnt_esperanca", envelope.TenantId);
        Assert.Equal("corr-001", envelope.CorrelationId);
        Assert.Equal(payload, envelope.Payload);
    }
}
