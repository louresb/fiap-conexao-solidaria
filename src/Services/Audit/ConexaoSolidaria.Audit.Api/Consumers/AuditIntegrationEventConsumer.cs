using ConexaoSolidaria.Audit.Api.Data;
using ConexaoSolidaria.Audit.Api.Security;
using ConexaoSolidaria.Contracts.Events;

using MassTransit;

using MongoDB.Driver;

namespace ConexaoSolidaria.Audit.Api.Consumers;

public sealed class AuditIntegrationEventConsumer : IConsumer<IntegrationEvent>
{
    private readonly AuditMongoContext _mongo;
    private readonly ILogger<AuditIntegrationEventConsumer> _logger;

    public AuditIntegrationEventConsumer(AuditMongoContext mongo, ILogger<AuditIntegrationEventConsumer> logger)
    {
        _mongo = mongo;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<IntegrationEvent> context)
    {
        var message = context.Message;
        var document = new AuditEventDocument
        {
            Id = message.EventId,
            TenantId = message.TenantId,
            EventType = message.EventType,
            Source = message.Source,
            CorrelationId = message.CorrelationId,
            CausationId = message.CausationId,
            PayloadJson = AuditPayloadRedactor.Redact(message.PayloadJson),
            Timestamp = message.OccurredAtUtc
        };

        try
        {
            await _mongo.Events.InsertOneAsync(document, cancellationToken: context.CancellationToken);
            _logger.LogInformation("Audit event {EventId} stored as {EventType}", message.EventId, message.EventType);
        }
        catch (MongoWriteException ex) when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey)
        {
            _logger.LogInformation("Audit event {EventId} already stored", message.EventId);
        }
    }
}
