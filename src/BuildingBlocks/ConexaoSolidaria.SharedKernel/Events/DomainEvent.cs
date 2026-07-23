using ConexaoSolidaria.SharedKernel.Primitives;

namespace ConexaoSolidaria.SharedKernel.Events;

public abstract record DomainEvent(
    Guid EventId,
    TenantId TenantId,
    string CorrelationId,
    DateTimeOffset OccurredAtUtc) : IDomainEvent;
