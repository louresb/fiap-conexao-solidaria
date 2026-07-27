using System.Text.Json;
using System.Text.Json.Nodes;

namespace ConexaoSolidaria.Audit.Api.Security;

public static class AuditPayloadRedactor
{
    private static readonly HashSet<string> SensitiveKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "authorization",
        "cpf",
        "email",
        "donorEmail",
        "password",
        "passwordHash",
        "secret",
        "token",
        "accessToken",
        "refreshToken"
    };

    public static string Redact(string payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return "{}";
        }

        try
        {
            var node = JsonNode.Parse(payloadJson);
            RedactNode(node);
            return node?.ToJsonString(new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? "{}";
        }
        catch (JsonException)
        {
            return "{\"redacted\":\"invalid-json\"}";
        }
    }

    private static void RedactNode(JsonNode? node)
    {
        if (node is JsonObject jsonObject)
        {
            foreach (var property in jsonObject.ToList())
            {
                if (SensitiveKeys.Contains(property.Key))
                {
                    jsonObject[property.Key] = "[REDACTED]";
                    continue;
                }

                RedactNode(property.Value);
            }
        }
        else if (node is JsonArray jsonArray)
        {
            foreach (var item in jsonArray)
            {
                RedactNode(item);
            }
        }
    }
}
