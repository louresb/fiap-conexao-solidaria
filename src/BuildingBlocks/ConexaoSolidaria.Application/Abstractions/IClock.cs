namespace ConexaoSolidaria.Application.Abstractions;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
