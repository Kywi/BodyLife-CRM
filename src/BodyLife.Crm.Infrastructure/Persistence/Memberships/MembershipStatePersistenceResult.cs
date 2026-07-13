using BodyLife.Crm.Modules.Memberships;

namespace BodyLife.Crm.Infrastructure.Persistence.Memberships;

public sealed class MembershipStatePersistenceResult
{
    private MembershipStatePersistenceResult(
        MembershipStatePersistenceStatus status,
        Guid membershipId,
        MembershipCalculatedState? state,
        int persistedExtensionRowCount,
        DateTimeOffset? recalculatedAt,
        int? recalculationVersion)
    {
        Status = status;
        MembershipId = membershipId;
        State = state;
        PersistedExtensionRowCount = persistedExtensionRowCount;
        RecalculatedAt = recalculatedAt;
        RecalculationVersion = recalculationVersion;
    }

    public MembershipStatePersistenceStatus Status { get; }

    public Guid MembershipId { get; }

    public bool Succeeded => Status == MembershipStatePersistenceStatus.Persisted;

    public MembershipCalculatedState? State { get; }

    public int PersistedExtensionRowCount { get; }

    public DateTimeOffset? RecalculatedAt { get; }

    public int? RecalculationVersion { get; }

    internal static MembershipStatePersistenceResult MissingMembership(Guid membershipId)
    {
        return new MembershipStatePersistenceResult(
            MembershipStatePersistenceStatus.MissingMembership,
            membershipId,
            state: null,
            persistedExtensionRowCount: 0,
            recalculatedAt: null,
            recalculationVersion: null);
    }

    internal static MembershipStatePersistenceResult Persisted(
        Guid membershipId,
        MembershipCalculatedState state,
        int persistedExtensionRowCount,
        DateTimeOffset recalculatedAt)
    {
        return new MembershipStatePersistenceResult(
            MembershipStatePersistenceStatus.Persisted,
            membershipId,
            state,
            persistedExtensionRowCount,
            recalculatedAt,
            MembershipStateCacheRebuilder.CurrentRecalculationVersion);
    }
}
