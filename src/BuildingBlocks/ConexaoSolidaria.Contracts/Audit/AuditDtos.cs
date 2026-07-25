namespace ConexaoSolidaria.Contracts.Audit;

public sealed record AuditEventDto(
    string Id,
    string TenantId,
    string EventType,
    string Source,
    string CorrelationId,
    string? CausationId,
    string PayloadJson,
    DateTimeOffset Timestamp);