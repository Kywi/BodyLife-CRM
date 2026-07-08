namespace BodyLife.Crm.SharedKernel;

public readonly record struct RequestCorrelationId(string Value)
{
    public override string ToString() => Value;
}
