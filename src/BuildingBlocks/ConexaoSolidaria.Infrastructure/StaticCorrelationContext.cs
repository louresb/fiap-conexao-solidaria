using ConexaoSolidaria.Application.Abstractions;

namespace ConexaoSolidaria.Infrastructure;

public sealed class StaticCorrelationContext(string correlationId) : ICorrelationContext
{
    public string CorrelationId { get; } = string.IsNullOrWhiteSpace(correlationId)
        ? Guid.NewGuid().ToString("N")
        : correlationId;
}
