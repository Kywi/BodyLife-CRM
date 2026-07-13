using BodyLife.Crm.Modules.Memberships;

namespace BodyLife.Crm.Infrastructure.Persistence.Memberships;

public sealed class MembershipStateCacheRebuildResult
{
    private MembershipStateCacheRebuildResult(
        MembershipStateCacheRebuildStatus status,
        Guid membershipId,
        MembershipCalculatedState? state,
        DateTimeOffset? recalculatedAt,
        int? recalculationVersion)
    {
        Status = status;
        MembershipId = membershipId;
        State = state;
        RecalculatedAt = recalculatedAt;
        RecalculationVersion = recalculationVersion;
    }

    public MembershipStateCacheRebuildStatus Status { get; }

    public Guid MembershipId { get; }

    public bool Succeeded => Status != MembershipStateCacheRebuildStatus.MissingSource;

    public bool DriftDetected => Status is MembershipStateCacheRebuildStatus.Created
        or MembershipStateCacheRebuildStatus.Repaired;

    public MembershipCalculatedState? State { get; }

    public DateTimeOffset? RecalculatedAt { get; }

    public int? RecalculationVersion { get; }

    internal static MembershipStateCacheRebuildResult MissingSource(Guid membershipId)
    {
        return new MembershipStateCacheRebuildResult(
            MembershipStateCacheRebuildStatus.MissingSource,
            membershipId,
            state: null,
            recalculatedAt: null,
            recalculationVersion: null);
    }

    internal static MembershipStateCacheRebuildResult Completed(
        MembershipStateCacheRebuildStatus status,
        Guid membershipId,
        MembershipCalculatedState state,
        DateTimeOffset recalculatedAt,
        int recalculationVersion)
    {
        return new MembershipStateCacheRebuildResult(
            status,
            membershipId,
            state,
            recalculatedAt,
            recalculationVersion);
    }
}
