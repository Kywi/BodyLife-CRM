namespace BodyLife.Crm.SharedKernel;

public readonly record struct AuditEntryId(Guid Value)
{
    public static AuditEntryId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString();
}
