namespace ConexaoSolidaria.SharedKernel.Primitives;

public readonly record struct TenantId
{
    public TenantId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Tenant id is required.", nameof(value));
        }

        Value = value.Trim();
    }

    public string Value { get; }

    public override string ToString() => Value;

    public static TenantId From(string value) => new(value);
}