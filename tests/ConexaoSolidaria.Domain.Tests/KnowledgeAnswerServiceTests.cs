using ConexaoSolidaria.Knowledge.Api.Retrieval;
using ConexaoSolidaria.Knowledge.Api.Services;

using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;

namespace ConexaoSolidaria.Tests;

public sealed class KnowledgeAnswerServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"conexao-knowledge-{Guid.NewGuid():N}");

    [Fact]
    public void Answer_returns_cited_source_when_evidence_exists()
    {
        var service = CreateService();

        var answer = service.Answer("esperanca-solidaria", "Como a ONG presta contas das doacoes?", "corr-1");

        Assert.True(answer.Answered);
        Assert.NotEmpty(answer.Sources);
        Assert.Equal("transparencia", answer.Sources[0].DocumentId);
    }

    [Fact]
    public void Answer_refuses_to_invent_without_a_source()
    {
        var service = CreateService();

        var answer = service.Answer("esperanca-solidaria", "Qual a previsao do tempo em Marte?", "corr-2");

        Assert.False(answer.Answered);
        Assert.Empty(answer.Sources);
    }

    [Fact]
    public async Task AnswerAsync_identifies_grounded_generation_and_model()
    {
        var generator = new StubGenerator(new GroundedGenerationResult(
            "A prestacao de contas e publicada com indicadores [transparencia].",
            "demo-model"));
        var service = CreateService(generator);

        var answer = await service.AnswerAsync(
            "esperanca-solidaria",
            "Como a ONG presta contas das doacoes?",
            "corr-3",
            CancellationToken.None);

        Assert.True(answer.Answered);
        Assert.Equal("grounded-generation", answer.AnswerMode);
        Assert.Equal("demo-model", answer.Model);
        Assert.NotEmpty(answer.Sources);
    }

    [Fact]
    public async Task AnswerAsync_uses_extractive_fallback_when_generation_is_unavailable()
    {
        var service = CreateService(new StubGenerator(null));

        var answer = await service.AnswerAsync(
            "esperanca-solidaria",
            "Como a ONG presta contas das doacoes?",
            "corr-4",
            CancellationToken.None);

        Assert.True(answer.Answered);
        Assert.Equal("extractive", answer.AnswerMode);
        Assert.Null(answer.Model);
    }

    [Theory]
    [InlineData("Resposta sem citacao.")]
    [InlineData("Resposta baseada em uma fonte inexistente [documento-inventado].")]
    public async Task AnswerAsync_rejects_generated_answers_without_retrieved_citations(string generatedAnswer)
    {
        var generator = new StubGenerator(new GroundedGenerationResult(generatedAnswer, "demo-model"));
        var service = CreateService(generator);

        var answer = await service.AnswerAsync(
            "esperanca-solidaria",
            "Como a ONG presta contas das doacoes?",
            "corr-5",
            CancellationToken.None);

        Assert.True(answer.Answered);
        Assert.Equal("extractive", answer.AnswerMode);
        Assert.Null(answer.Model);
    }

    private KnowledgeAnswerService CreateService(IGroundedAnswerGenerator? generator = null)
    {
        var tenantPath = Path.Combine(_root, "esperanca-solidaria");
        Directory.CreateDirectory(tenantPath);
        File.WriteAllText(
            Path.Combine(tenantPath, "transparencia.md"),
            "# Politica de transparencia\n\nA ONG publica prestacao de contas, notas fiscais e indicadores de cada campanha para todos os doadores.");

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Knowledge:RootPath"] = _root,
                ["Knowledge:MinimumScore"] = "0.2"
            })
            .Build();
        var retriever = new KnowledgeRetriever(new TestWebHostEnvironment(_root), configuration);
        return new KnowledgeAnswerService(
            retriever,
            generator ?? DisabledGroundedAnswerGenerator.Instance,
            NullLogger<KnowledgeAnswerService>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private sealed class TestWebHostEnvironment(string contentRootPath) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "ConexaoSolidaria.Tests";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = contentRootPath;
        public string EnvironmentName { get; set; } = "Testing";
        public string ContentRootPath { get; set; } = contentRootPath;
        public IFileProvider ContentRootFileProvider { get; set; } = new PhysicalFileProvider(contentRootPath);
    }

    private sealed class StubGenerator(GroundedGenerationResult? result) : IGroundedAnswerGenerator
    {
        public Task<GroundedGenerationResult?> GenerateAsync(
            string question,
            IReadOnlyList<ConexaoSolidaria.Contracts.Knowledge.KnowledgeSourceDto> sources,
            CancellationToken cancellationToken)
            => Task.FromResult(result);
    }
}
