using System.Text.RegularExpressions;

using ConexaoSolidaria.Contracts.Knowledge;
using ConexaoSolidaria.Knowledge.Api.Retrieval;

using Microsoft.Extensions.Logging.Abstractions;

namespace ConexaoSolidaria.Knowledge.Api.Services;

public sealed class KnowledgeAnswerService(
    IKnowledgeRetriever retriever,
    IGroundedAnswerGenerator generator,
    ILogger<KnowledgeAnswerService> logger)
{
    private static readonly Regex CitationPattern = new(
        @"\[(?<documentId>[a-zA-Z0-9._-]+)\]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));

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
            if (generated is not null && HasValidCitations(generated.Answer, sources))
            {
                return new KnowledgeAnswerDto(
                    generated.Answer,
                    true,
                    sources,
                    correlationId,
                    "grounded-generation",
                    generated.Model);
            }

            if (generated is not null)
            {
                logger.LogWarning(
                    "Grounded generation returned missing or unknown citations; using extractive fallback. " +
                    "TenantId={TenantId} CorrelationId={CorrelationId}",
                    tenantId,
                    correlationId);
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

    private static bool HasValidCitations(
        string answer,
        IReadOnlyList<KnowledgeSourceDto> sources)
    {
        var citations = CitationPattern.Matches(answer)
            .Select(match => match.Groups["documentId"].Value)
            .ToArray();
        if (citations.Length == 0)
        {
            return false;
        }

        var knownDocuments = sources
            .Select(source => source.DocumentId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return citations.All(knownDocuments.Contains);
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
