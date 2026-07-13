namespace BodyLife.Crm.Modules.Memberships;

public sealed record MembershipExtensionDay
{
    internal MembershipExtensionDay(
        DateOnly extensionDate,
        MembershipExtensionSourceRange source)
    {
        ExtensionDate = extensionDate;
        SourceType = source.SourceType;
        SourceId = source.SourceId;
        SourceLabel = source.SourceLabel;
        IsActive = source.IsActive;
    }

    public DateOnly ExtensionDate { get; }

    public string SourceType { get; }

    public Guid SourceId { get; }

    public string SourceLabel { get; }

    public bool IsActive { get; }
}
