namespace ConexaoSolidaria.Application.Results;

public sealed record UseCaseResult(bool Succeeded, string? ErrorCode = null, string? ErrorMessage = null)
{
    public static UseCaseResult Success() => new(true);

    public static UseCaseResult Failure(string errorCode, string errorMessage) =>
        new(false, errorCode, errorMessage);
}