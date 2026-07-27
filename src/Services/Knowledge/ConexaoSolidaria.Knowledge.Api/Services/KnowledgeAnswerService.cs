using ConexaoSolidaria.Contracts.Knowledge;
using ConexaoSolidaria.Knowledge.Api.Retrieval;

using Microsoft.Extensions.Logging.Abstractions;

namespace ConexaoSolidaria.Knowledge.Api.Services;

public sealed class KnowledgeAnswerService(
    IKnowledgeRetriever retriever,
    IGroundedAnswerGenerator generator,
    ILogger<KnowledgeAnswerService> logger)
{
    public KnowledgeAnswerService(IKnowledgeRetriever retriever)
        : this(retriever, DisabledGroundedAnswerGenerator.Instance, NullLogger<KnowledgeAnswerService>.Instance)
    {
    }

    public KnowledgeAnswerDto Answer(string tenantId, string question, string correlationId)
        => CreateExtractiveAnswer(retriever.Search(tenantId, question), correlationId);

    public async Task<KnowledgeAnswerDto> AnswerAsync(
        string tenantId,
        string question,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var sources = retriever.Search(tenantId, question);
        if (sources.Count == 0)
        {
            return new KnowledgeAnswerDto(
                "Nao encontrei informacao suficiente nas fontes publicadas pela ONG para responder com seguranca.",
                false,
                [],
                correlationId);
        }

        try
        {
            var generated = await generator.GenerateAsync(question, sources, cancellationToken);
            if (generated is not null)
            {
                return new KnowledgeAnswerDto(
                    generated.Answer,
                    true,
                    sources,
                    correlationId,
                    "grounded-generation",
                    generated.Model);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(
                exception,
                "Grounded generation failed; using extractive fallback. TenantId={TenantId} CorrelationId={CorrelationId}",
                tenantId,
                correlationId);
        }

        return CreateExtractiveAnswer(sources, correlationId);
    }

    private static KnowledgeAnswerDto CreateExtractiveAnswer(
        IReadOnlyList<KnowledgeSourceDto> sources,
        string correlationId)
    {
        if (sources.Count == 0)
        {
            return new KnowledgeAnswerDto(
                "Nao encontrei informacao suficiente nas fontes publicadas pela ONG para responder com seguranca.",
                false,
                [],
                correlationId);
        }

        var answer = string.Join(" ", sources.Select(source => source.Excerpt));
        return new KnowledgeAnswerDto(answer, true, sources, correlationId);
    }
}
