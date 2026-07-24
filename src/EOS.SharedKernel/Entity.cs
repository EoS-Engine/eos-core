namespace EOS.SharedKernel;

public abstract class Entity
{
    public EntityId Id { get; }

    protected Entity(EntityId id)
    {
        Id = id;
    }

    public override bool Equals(object? obj)
    {
        return obj is Entity other && other.GetType() == GetType() && other.Id == Id;
    }

    public override int GetHashCode() => HashCode.Combine(GetType(), Id);
}
