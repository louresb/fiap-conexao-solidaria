using System.Net.Http.Headers;
using System.Net.Http.Json;

using ConexaoSolidaria.Contracts.Auth;
using ConexaoSolidaria.Contracts.Identity;

using Microsoft.Extensions.Options;

namespace ConexaoSolidaria.Identity.Api.Keycloak;

public sealed class KeycloakOptions
{
    public bool Enabled { get; set; }
    public string BaseUrl { get; set; } = "http://localhost:8080";
    public string Realm { get; set; } = "conexao-solidaria";
    public string AdminUser { get; set; } = "admin";
    public string AdminPassword { get; set; } = string.Empty;
}

public sealed class KeycloakUserProvisioner : IKeycloakUserProvisioner
{
    private readonly HttpClient _httpClient;
    private readonly KeycloakOptions _options;
    private readonly ILogger<KeycloakUserProvisioner> _logger;

    public KeycloakUserProvisioner(
        HttpClient httpClient,
        IOptions<KeycloakOptions> options,
        ILogger<KeycloakUserProvisioner> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task ProvisionDonorAsync(DonorRegistrationRequest request, string tenantId, CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            return;
        }

        var token = await GetAdminTokenAsync(cancellationToken);
        using var message = CreateAdminRequest(HttpMethod.Post, $"users", token);
        message.Content = JsonContent.Create(new
        {
            username = request.Email,
            email = request.Email,
            firstName = request.FullName,
            enabled = true,
            emailVerified = true,
            attributes = new Dictionary<string, string[]>
            {
                [AuthDefaults.TenantClaim] = [tenantId],
                ["cpf"] = [request.Cpf]
            },
            credentials = new[]
            {
                new
                {
                    type = "password",
                    value = request.Password,
                    temporary = false
                }
            }
        });

        using var response = await _httpClient.SendAsync(message, cancellationToken);
        if (!response.IsSuccessStatusCode && response.StatusCode != System.Net.HttpStatusCode.Conflict)
        {
            _logger.LogWarning("Keycloak user provisioning failed with status {StatusCode}", response.StatusCode);
            response.EnsureSuccessStatusCode();
        }

        var userId = response.Headers.Location?.Segments.LastOrDefault()?.Trim('/');
        if (string.IsNullOrWhiteSpace(userId))
        {
            userId = await FindUserIdAsync(request.Email, token, cancellationToken);
        }

        await AssignDonorRoleAsync(userId, token, cancellationToken);
        _logger.LogInformation("Keycloak donor {UserId} provisioned for tenant {TenantId}", userId, tenantId);
    }

    private async Task<string> GetAdminTokenAsync(CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = "admin-cli",
            ["grant_type"] = "password",
            ["username"] = _options.AdminUser,
            ["password"] = _options.AdminPassword
        });

        var response = await _httpClient.PostAsync($"{_options.BaseUrl}/realms/master/protocol/openid-connect/token", content, cancellationToken);
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>(cancellationToken);
        return payload?["access_token"]?.ToString() ?? throw new InvalidOperationException("Keycloak did not return access_token.");
    }

    private async Task<string> FindUserIdAsync(string email, string token, CancellationToken cancellationToken)
    {
        using var request = CreateAdminRequest(HttpMethod.Get, $"users?username={Uri.EscapeDataString(email)}&exact=true", token);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var users = await response.Content.ReadFromJsonAsync<List<KeycloakUser>>(cancellationToken);
        return users?.SingleOrDefault()?.Id
            ?? throw new InvalidOperationException("Keycloak user was not found after provisioning.");
    }

    private async Task AssignDonorRoleAsync(string userId, string token, CancellationToken cancellationToken)
    {
        using var roleRequest = CreateAdminRequest(HttpMethod.Get, $"roles/{AuthDefaults.DonorRole}", token);
        using var roleResponse = await _httpClient.SendAsync(roleRequest, cancellationToken);
        roleResponse.EnsureSuccessStatusCode();
        var role = await roleResponse.Content.ReadFromJsonAsync<KeycloakRole>(cancellationToken)
            ?? throw new InvalidOperationException("Keycloak donor role was not found.");

        using var assignment = CreateAdminRequest(HttpMethod.Post, $"users/{userId}/role-mappings/realm", token);
        assignment.Content = JsonContent.Create(new[] { role });
        using var assignmentResponse = await _httpClient.SendAsync(assignment, cancellationToken);
        assignmentResponse.EnsureSuccessStatusCode();
    }

    private HttpRequestMessage CreateAdminRequest(HttpMethod method, string path, string token)
    {
        var request = new HttpRequestMessage(method, $"{_options.BaseUrl}/admin/realms/{_options.Realm}/{path}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private sealed record KeycloakUser(string Id);

    private sealed record KeycloakRole(string Id, string Name);
}