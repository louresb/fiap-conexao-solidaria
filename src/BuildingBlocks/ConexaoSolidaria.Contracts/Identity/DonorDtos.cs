namespace ConexaoSolidaria.Contracts.Identity;

public sealed record DonorRegistrationRequest(
    string FullName,
    string Email,
    string Cpf,
    string Password,
    string? TenantId = null);

public sealed record DonorProfileDto(
    Guid Id,
    string TenantId,
    string FullName,
    string Email,
    string MaskedCpf);