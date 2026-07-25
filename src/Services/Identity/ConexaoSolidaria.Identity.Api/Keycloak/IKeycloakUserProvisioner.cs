using ConexaoSolidaria.Contracts.Identity;

namespace ConexaoSolidaria.Identity.Api.Keycloak;

public interface IKeycloakUserProvisioner
{
    Task ProvisionDonorAsync(DonorRegistrationRequest request, string tenantId, CancellationToken cancellationToken);
}