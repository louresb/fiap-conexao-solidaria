namespace ConexaoSolidaria.Contracts.Knowledge;

public sealed record AskKnowledgeRequest(string Question);

public sealed record KnowledgeAnswerDto(
    string Answer,
    bool Answered,
    IReadOnlyList<KnowledgeSourceDto> Sources,
    string CorrelationId);

public sealed record KnowledgeSourceDto(
    string DocumentId,
    string Title,
    string Excerpt,
    double Score);