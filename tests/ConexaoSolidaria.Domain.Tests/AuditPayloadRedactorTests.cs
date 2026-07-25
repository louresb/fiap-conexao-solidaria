using ConexaoSolidaria.Audit.Api.Security;

namespace ConexaoSolidaria.Tests;

public sealed class AuditPayloadRedactorTests
{
    [Fact]
    public void Redact_removes_sensitive_values_at_any_depth()
    {
        const string payload = """
            {"email":"doador@example.org","cpf":"39053344705","nested":{"accessToken":"secret-token"}}
            """;

        var redacted = AuditPayloadRedactor.Redact(payload);

        Assert.Contains("doador@example.org", redacted);
        Assert.DoesNotContain("39053344705", redacted);
        Assert.DoesNotContain("secret-token", redacted);
        Assert.Contains("[REDACTED]", redacted);
    }

    [Fact]
    public void Redact_does_not_persist_invalid_raw_payload()
    {
        var redacted = AuditPayloadRedactor.Redact("not-json-with-password");

        Assert.Equal("{\"redacted\":\"invalid-json\"}", redacted);
    }
}
