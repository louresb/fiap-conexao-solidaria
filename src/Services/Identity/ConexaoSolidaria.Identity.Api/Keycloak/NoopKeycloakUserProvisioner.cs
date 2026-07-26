using ConexaoSolidaria.Contracts.Identity;

namespace ConexaoSolidaria.Identity.Api.Keycloak;

public sealed class NoopKeycloakUserProvisioner : IKeycloakUserProvisioner
{
    public Task ProvisionDonorAsync(DonorRegistrationRequest request, string tenantId, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
