using System.Net;
using System.Text;
using System.Text.Json;

using ConexaoSolidaria.Contracts.Identity;
using ConexaoSolidaria.Identity.Api.Keycloak;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ConexaoSolidaria.Tests;

public sealed class KeycloakUserProvisionerTests
{
    [Fact]
    public async Task ProvisionDonorAsync_sends_a_complete_profile_and_assigns_the_donor_role()
    {
        var handler = new KeycloakAdminHandler();
        using var httpClient = new HttpClient(handler);
        var provisioner = new KeycloakUserProvisioner(
            httpClient,
            Options.Create(new KeycloakOptions
            {
                Enabled = true,
                BaseUrl = "https://identity.example.test",
                Realm = "conexao-solidaria",
                AdminUser = "admin",
                AdminPassword = "not-a-real-secret"
            }),
            NullLogger<KeycloakUserProvisioner>.Instance);

        await provisioner.ProvisionDonorAsync(
            new DonorRegistrationRequest(
                "  Doador   E2E Conexão Solidária  ",
                "doador@example.test",
                "52998224725",
                "SolidariaE2e2026"),
            "esperanca-solidaria",
            CancellationToken.None);

        using var document = JsonDocument.Parse(handler.UserPayload!);
        var user = document.RootElement;
        Assert.Equal("Doador", user.GetProperty("firstName").GetString());
        Assert.Equal("E2E Conexão Solidária", user.GetProperty("lastName").GetString());
        Assert.Equal(
            "esperanca-solidaria",
            user.GetProperty("attributes").GetProperty("tenant_id")[0].GetString());
        Assert.Equal("52998224725", user.GetProperty("attributes").GetProperty("cpf")[0].GetString());
        Assert.False(user.GetProperty("credentials")[0].GetProperty("temporary").GetBoolean());
        Assert.Equal("Doador", handler.AssignedRole);
    }

    private sealed class KeycloakAdminHandler : HttpMessageHandler
    {
        private int _requestIndex;

        public string? UserPayload { get; private set; }
        public string? AssignedRole { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return _requestIndex++ switch
            {
                0 => Json(HttpStatusCode.OK, "{\"access_token\":\"admin-token\"}"),
                1 => await CaptureUserAsync(request, cancellationToken),
                2 => Json(HttpStatusCode.OK, "{\"id\":\"donor-role-id\",\"name\":\"Doador\"}"),
                3 => await CaptureRoleAsync(request, cancellationToken),
                _ => throw new InvalidOperationException($"Unexpected Keycloak request: {request.Method} {request.RequestUri}")
            };
        }

        private async Task<HttpResponseMessage> CaptureUserAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.EndsWith("/admin/realms/conexao-solidaria/users", request.RequestUri!.AbsoluteUri);
            UserPayload = await request.Content!.ReadAsStringAsync(cancellationToken);

            var response = new HttpResponseMessage(HttpStatusCode.Created);
            response.Headers.Location = new Uri(
                "https://identity.example.test/admin/realms/conexao-solidaria/users/donor-user-id");
            return response;
        }

        private async Task<HttpResponseMessage> CaptureRoleAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.EndsWith(
                "/admin/realms/conexao-solidaria/users/donor-user-id/role-mappings/realm",
                request.RequestUri!.AbsoluteUri);
            var payload = await request.Content!.ReadAsStringAsync(cancellationToken);
            using var document = JsonDocument.Parse(payload);
            AssignedRole = document.RootElement[0].GetProperty("name").GetString();
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        }

        private static HttpResponseMessage Json(HttpStatusCode statusCode, string payload) => new(statusCode)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
    }
}
