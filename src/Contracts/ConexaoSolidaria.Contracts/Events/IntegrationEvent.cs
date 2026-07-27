namespace ConexaoSolidaria.Contracts.Events;

public sealed record IntegrationEvent(
    Guid EventId,
    string EventType,
    string TenantId,
    string CorrelationId,
    string? CausationId,
    DateTimeOffset OccurredAtUtc,
    string Source,
    string PayloadJson)
{
    public static IntegrationEvent Create(
        string eventType,
        string tenantId,
        string correlationId,
        string source,
        string payloadJson,
        string? causationId = null)
    {
        return new IntegrationEvent(
            Guid.NewGuid(),
            eventType,
            tenantId,
            correlationId,
            causationId,
            DateTimeOffset.UtcNow,
            source,
            payloadJson);
    }
}
