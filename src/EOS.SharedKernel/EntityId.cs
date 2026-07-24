namespace EOS.SharedKernel;

public readonly record struct EntityId(Guid Value)
{
    public static EntityId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString();
}
