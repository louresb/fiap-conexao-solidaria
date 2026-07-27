using System.Net;
using System.Text;
using System.Text.Json;

using ConexaoSolidaria.Contracts.Knowledge;
using ConexaoSolidaria.Knowledge.Api.Services;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ConexaoSolidaria.Tests;

public sealed class GroundedAnswerGeneratorTests
{
    private static readonly IReadOnlyList<KnowledgeSourceDto> Sources =
    [
        new(
            "transparencia",
            "Politica de transparencia",
            "A ONG publica indicadores e comprovantes de cada campanha.",
            0.95)
    ];

    [Fact]
    public async Task GenerateAsync_sends_an_openai_compatible_grounded_request()
    {
        var handler = new RecordingHandler(
            HttpStatusCode.OK,
            """{"choices":[{"message":{"content":"A prestacao de contas e publica [transparencia]."}}]}""");
        var generator = CreateGenerator(handler);

        var result = await generator.GenerateAsync(
            "Como a ONG presta contas?",
            Sources,
            CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("gpt-4o-mini", result.Model);
        Assert.Equal("A prestacao de contas e publica [transparencia].", result.Answer);
        Assert.Equal("test-key", handler.ApiKey);
        Assert.Equal(
            "https://example.services.ai.azure.com/openai/v1/chat/completions",
            handler.RequestUri?.AbsoluteUri);

        using var payload = JsonDocument.Parse(handler.Body!);
        Assert.Equal("gpt-4o-mini", payload.RootElement.GetProperty("model").GetString());
        Assert.Equal(350, payload.RootElement.GetProperty("max_tokens").GetInt32());

        var messages = payload.RootElement.GetProperty("messages");
        var prompt = messages[1].GetProperty("content").GetString();
        Assert.Contains("Como a ONG presta contas?", prompt, StringComparison.Ordinal);
        Assert.Contains("[DOCUMENTO:transparencia]", prompt, StringComparison.Ordinal);
        Assert.Contains(Sources[0].Excerpt, prompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GenerateAsync_supports_the_current_completion_token_parameter()
    {
        var handler = new RecordingHandler(
            HttpStatusCode.OK,
            """{"choices":[{"message":{"content":"Resposta fundamentada [transparencia]."}}]}""");
        var generator = CreateGenerator(handler, tokenLimitParameter: "max_completion_tokens");

        await generator.GenerateAsync("Como funciona?", Sources, CancellationToken.None);

        using var payload = JsonDocument.Parse(handler.Body!);
        Assert.Equal(350, payload.RootElement.GetProperty("max_completion_tokens").GetInt32());
        Assert.False(payload.RootElement.TryGetProperty("max_tokens", out _));
    }

    [Fact]
    public async Task GenerateAsync_supports_bearer_authentication()
    {
        var handler = new RecordingHandler(
            HttpStatusCode.OK,
            """{"choices":[{"message":{"content":"Resposta fundamentada [transparencia]."}}]}""");
        var generator = CreateGenerator(handler, apiKeyHeader: "Authorization");

        await generator.GenerateAsync("Como funciona?", Sources, CancellationToken.None);

        Assert.Equal("Bearer", handler.AuthorizationScheme);
        Assert.Equal("test-key", handler.AuthorizationParameter);
        Assert.Null(handler.ApiKey);
    }

    [Fact]
    public async Task GenerateAsync_returns_null_when_the_provider_declines_the_sources()
    {
        var handler = new RecordingHandler(
            HttpStatusCode.OK,
            """{"choices":[{"message":{"content":"FONTE_INSUFICIENTE"}}]}""");
        var generator = CreateGenerator(handler);

        var result = await generator.GenerateAsync(
            "Pergunta sem cobertura",
            Sources,
            CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task GenerateAsync_rejects_a_non_https_endpoint()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, "{}");
        var generator = CreateGenerator(handler, endpoint: "http://insecure.example/chat/completions");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => generator.GenerateAsync(
            "Como funciona?",
            Sources,
            CancellationToken.None));

        Assert.Contains("absolute HTTPS", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, handler.CallCount);
    }

    private static OpenAiCompatibleGroundedAnswerGenerator CreateGenerator(
        HttpMessageHandler handler,
        string endpoint = "https://example.services.ai.azure.com/openai/v1/chat/completions",
        string apiKeyHeader = "api-key",
        string tokenLimitParameter = "max_tokens")
    {
        var options = Options.Create(new GroundedGenerationOptions
        {
            Enabled = true,
            Endpoint = endpoint,
            ApiKey = "test-key",
            ApiKeyHeader = apiKeyHeader,
            Model = "gpt-4o-mini",
            MaximumOutputTokens = 350,
            TokenLimitParameter = tokenLimitParameter
        });

        return new OpenAiCompatibleGroundedAnswerGenerator(
            new HttpClient(handler),
            options,
            NullLogger<OpenAiCompatibleGroundedAnswerGenerator>.Instance);
    }

    private sealed class RecordingHandler(HttpStatusCode statusCode, string responseBody) : HttpMessageHandler
    {
        public int CallCount { get; private set; }
        public Uri? RequestUri { get; private set; }
        public string? Body { get; private set; }
        public string? ApiKey { get; private set; }
        public string? AuthorizationScheme { get; private set; }
        public string? AuthorizationParameter { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            RequestUri = request.RequestUri;
            Body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            ApiKey = request.Headers.TryGetValues("api-key", out var values)
                ? values.Single()
                : null;
            AuthorizationScheme = request.Headers.Authorization?.Scheme;
            AuthorizationParameter = request.Headers.Authorization?.Parameter;

            return new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
            };
        }
    }
}
