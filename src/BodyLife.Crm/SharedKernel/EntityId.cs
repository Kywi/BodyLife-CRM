namespace BodyLife.Crm.SharedKernel;

public readonly record struct EntityId(string Type, Guid Value)
{
    public override string ToString() => $"{Type}:{Value}";
}
