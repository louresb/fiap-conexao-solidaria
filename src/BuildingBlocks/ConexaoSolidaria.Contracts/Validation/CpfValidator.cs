namespace ConexaoSolidaria.Contracts.Validation;

public static class CpfValidator
{
    public static bool IsValid(string? cpf)
    {
        if (string.IsNullOrWhiteSpace(cpf))
        {
            return false;
        }

        var digits = new string(cpf.Where(char.IsDigit).ToArray());
        if (digits.Length != 11 || digits.Distinct().Count() == 1)
        {
            return false;
        }

        var sum = 0;
        for (var i = 0; i < 9; i++)
        {
            sum += (digits[i] - '0') * (10 - i);
        }

        var firstCheck = sum % 11;
        firstCheck = firstCheck < 2 ? 0 : 11 - firstCheck;

        sum = 0;
        for (var i = 0; i < 10; i++)
        {
            sum += (digits[i] - '0') * (11 - i);
        }

        var secondCheck = sum % 11;
        secondCheck = secondCheck < 2 ? 0 : 11 - secondCheck;

        return digits[9] - '0' == firstCheck && digits[10] - '0' == secondCheck;
    }

    public static string Normalize(string cpf)
    {
        return new string(cpf.Where(char.IsDigit).ToArray());
    }
}
