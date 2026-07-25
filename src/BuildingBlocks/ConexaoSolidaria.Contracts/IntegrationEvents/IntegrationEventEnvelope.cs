namespace ConexaoSolidaria.Contracts.IntegrationEvents;

public sealed record IntegrationEventEnvelope<TPayload>(
    Guid EventId,
    string EventType,
    string TenantId,
    string CorrelationId,
    string? CausationId,
    DateTimeOffset OccurredAtUtc,
    TPayload Payload)
{
    public static IntegrationEventEnvelope<TPayload> Create(
        string eventType,
        string tenantId,
        string correlationId,
        TPayload payload,
        string? causationId = null,
        DateTimeOffset? occurredAtUtc = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventType);
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);

        return new IntegrationEventEnvelope<TPayload>(
            Guid.NewGuid(),
            eventType,
            tenantId,
            correlationId,
            causationId,
            occurredAtUtc ?? DateTimeOffset.UtcNow,
            payload);
    }
}