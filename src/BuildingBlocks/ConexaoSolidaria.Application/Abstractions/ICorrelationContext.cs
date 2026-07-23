namespace ConexaoSolidaria.Application.Abstractions;

public interface ICorrelationContext
{
    string CorrelationId { get; }
}
