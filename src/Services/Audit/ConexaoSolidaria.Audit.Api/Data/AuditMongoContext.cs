using MongoDB.Driver;

namespace ConexaoSolidaria.Audit.Api.Data;

public sealed class AuditMongoContext
{
    public AuditMongoContext(IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Mongo") ?? "mongodb://localhost:27017";
        var databaseName = configuration["Mongo:Database"] ?? "conexao_audit";
        var client = new MongoClient(connectionString);
        Database = client.GetDatabase(databaseName);
        Events = Database.GetCollection<AuditEventDocument>("audit_events");
    }

    public IMongoDatabase Database { get; }
    public IMongoCollection<AuditEventDocument> Events { get; }

    public async Task EnsureIndexesAsync(CancellationToken cancellationToken = default)
    {
        var indexes = new[]
        {
            new CreateIndexModel<AuditEventDocument>(
                Builders<AuditEventDocument>.IndexKeys
                    .Ascending(item => item.TenantId)
                    .Descending(item => item.Timestamp)),
            new CreateIndexModel<AuditEventDocument>(
                Builders<AuditEventDocument>.IndexKeys
                    .Ascending(item => item.TenantId)
                    .Ascending(item => item.CorrelationId))
        };

        await Events.Indexes.CreateManyAsync(indexes, cancellationToken);
    }
}
