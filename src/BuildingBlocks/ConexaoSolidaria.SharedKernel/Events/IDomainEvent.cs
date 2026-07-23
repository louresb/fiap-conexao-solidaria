using ConexaoSolidaria.SharedKernel.Primitives;

namespace ConexaoSolidaria.SharedKernel.Events;

public interface IDomainEvent
{
    Guid EventId { get; }

    TenantId TenantId { get; }

    string CorrelationId { get; }

    DateTimeOffset OccurredAtUtc { get; }
}
