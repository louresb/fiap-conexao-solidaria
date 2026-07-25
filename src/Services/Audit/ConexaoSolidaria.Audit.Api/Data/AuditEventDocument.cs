using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace ConexaoSolidaria.Audit.Api.Data;

public sealed class AuditEventDocument
{
    [BsonId]
    [BsonRepresentation(BsonType.String)]
    public Guid Id { get; set; }

    public string TenantId { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
    public string? CausationId { get; set; }
    public string PayloadJson { get; set; } = string.Empty;
    public DateTimeOffset Timestamp { get; set; }
}