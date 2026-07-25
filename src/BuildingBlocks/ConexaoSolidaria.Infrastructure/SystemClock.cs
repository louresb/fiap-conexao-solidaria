using ConexaoSolidaria.Application.Abstractions;

namespace ConexaoSolidaria.Infrastructure;

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}