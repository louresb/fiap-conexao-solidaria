namespace ConexaoSolidaria.Contracts.Events;

public sealed record KnowledgeQuestionAnsweredPayload(
    string Question,
    bool Answered,
    IReadOnlyList<string> SourceDocumentIds,
    string TenantId,
    string AnswerMode,
    string? Model);
