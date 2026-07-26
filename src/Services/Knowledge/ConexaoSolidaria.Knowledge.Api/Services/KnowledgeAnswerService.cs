using ConexaoSolidaria.Contracts.Knowledge;
using ConexaoSolidaria.Knowledge.Api.Retrieval;

namespace ConexaoSolidaria.Knowledge.Api.Services;

public sealed class KnowledgeAnswerService(IKnowledgeRetriever retriever)
{
    public KnowledgeAnswerDto Answer(string tenantId, string question, string correlationId)
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

        var answer = string.Join(" ", sources.Select(source => source.Excerpt));
        return new KnowledgeAnswerDto(answer, true, sources, correlationId);
    }
}