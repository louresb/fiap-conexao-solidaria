namespace ConexaoSolidaria.Application.Abstractions;

public interface ICurrentTenant
{
    string TenantId { get; }
}