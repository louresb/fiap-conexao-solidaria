namespace ConexaoSolidaria.SharedKernel.Primitives;

public readonly record struct EntityId
{
    public EntityId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Entity id cannot be empty.", nameof(value));
        }

        Value = value;
    }

    public Guid Value { get; }

    public override string ToString() => Value.ToString("D");

    public static EntityId New() => new(Guid.NewGuid());

    public static EntityId From(Guid value) => new(value);
}
