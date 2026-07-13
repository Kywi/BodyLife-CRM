namespace BodyLife.Crm.Infrastructure.Persistence.Memberships;

public sealed class MembershipExtensionDayWriteResult
{
    private MembershipExtensionDayWriteResult(
        MembershipExtensionDayWriteStatus status,
        Guid membershipId,
        int? extensionDays,
        int persistedRowCount,
        DateTimeOffset? recalculatedAt)
    {
        Status = status;
        MembershipId = membershipId;
        ExtensionDays = extensionDays;
        PersistedRowCount = persistedRowCount;
        RecalculatedAt = recalculatedAt;
    }

    public MembershipExtensionDayWriteStatus Status { get; }

    public Guid MembershipId { get; }

    public bool Succeeded => Status == MembershipExtensionDayWriteStatus.Replaced;

    public int? ExtensionDays { get; }

    public int PersistedRowCount { get; }

    public DateTimeOffset? RecalculatedAt { get; }

    internal static MembershipExtensionDayWriteResult MissingMembership(Guid membershipId)
    {
        return new MembershipExtensionDayWriteResult(
            MembershipExtensionDayWriteStatus.MissingMembership,
            membershipId,
            extensionDays: null,
            persistedRowCount: 0,
            recalculatedAt: null);
    }

    internal static MembershipExtensionDayWriteResult Replaced(
        Guid membershipId,
        int extensionDays,
        int persistedRowCount,
        DateTimeOffset recalculatedAt)
    {
        return new MembershipExtensionDayWriteResult(
            MembershipExtensionDayWriteStatus.Replaced,
            membershipId,
            extensionDays,
            persistedRowCount,
            recalculatedAt);
    }
}
