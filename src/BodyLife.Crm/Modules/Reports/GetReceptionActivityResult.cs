using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Modules.Memberships;

namespace BodyLife.Crm.Modules.Reports;

public enum GetReceptionActivityStatus
{
    Success = 1,
    PermissionDenied,
    ValidationFailed,
    RecalculationFailed,
    SourceInconsistent,
}

public enum ReceptionActivityEventType
{
    ClientCreated = 1,
    ClientUpdated,
    CardAssigned,
    CardChanged,
    CardCleared,
    MembershipIssued,
    MembershipOpeningStateCreated,
    VisitMarked,
    VisitCanceled,
    PaymentCreated,
    PaymentCorrected,
    PaymentCanceled,
    FreezeAdded,
    FreezeCanceled,
    MembershipReplaced,
    MembershipSaleCanceled,
}

public enum ReceptionActivityMembershipSelectionStatus
{
    None = 1,
    Single,
    Ambiguous,
}

public enum ReceptionActivityRelatedEntityType
{
    Membership = 1,
    Payment,
    Visit,
    Freeze,
    CardAssignment,
}

public sealed record ReceptionActivityRelatedEntity(
    ReceptionActivityRelatedEntityType Type,
    Guid Id);

public sealed record ReceptionActivityMembershipCandidate(
    Guid MembershipId,
    int RemainingVisits,
    int NegativeBalance,
    DateOnly EffectiveEndDate,
    IReadOnlyList<MembershipWarning> Warnings);

public sealed class ReceptionActivityMembershipState
{
    private ReceptionActivityMembershipState(
        ReceptionActivityMembershipSelectionStatus selectionStatus,
        ReceptionActivityMembershipCandidate? timelineState,
        IReadOnlyList<ReceptionActivityMembershipCandidate> candidates)
    {
        SelectionStatus = selectionStatus;
        TimelineState = timelineState;
        Candidates = candidates;
    }

    public ReceptionActivityMembershipSelectionStatus SelectionStatus { get; }
    public ReceptionActivityMembershipCandidate? TimelineState { get; }
    public IReadOnlyList<ReceptionActivityMembershipCandidate> Candidates { get; }

    public static ReceptionActivityMembershipState Create(
        ReceptionActivityMembershipSelectionStatus selectionStatus,
        ReceptionActivityMembershipCandidate? timelineState,
        IEnumerable<ReceptionActivityMembershipCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        var snapshot = candidates.ToArray();
        if (!Enum.IsDefined(selectionStatus)
            || snapshot.Any(candidate => candidate is null || candidate.MembershipId == Guid.Empty)
            || timelineState?.MembershipId == Guid.Empty
            || (selectionStatus == ReceptionActivityMembershipSelectionStatus.Single
                && (timelineState is null || snapshot.Length != 1 || timelineState.MembershipId != snapshot[0].MembershipId))
            || (selectionStatus == ReceptionActivityMembershipSelectionStatus.Ambiguous
                && (timelineState is not null || snapshot.Length < 2))
            || (selectionStatus == ReceptionActivityMembershipSelectionStatus.None
                && snapshot.Length != 0))
        {
            throw new ArgumentException("Membership activity state selection is inconsistent.");
        }

        return new ReceptionActivityMembershipState(selectionStatus, timelineState, Array.AsReadOnly(snapshot));
    }
}

public sealed record ReceptionActivityItem(
    ReceptionActivityEventType EventType,
    Guid AuditEntryId,
    Guid SourceEntityId,
    Guid ClientId,
    string ClientDisplayName,
    IReadOnlyList<ReceptionActivityRelatedEntity> RelatedEntities,
    DateTimeOffset OccurredAt,
    DateTimeOffset RecordedAt,
    EntryOrigin EntryOrigin,
    bool IsCorrectionOrCancellation,
    bool ChangedAfterClose,
    ReceptionActivityMembershipState MembershipState);

public sealed class ReceptionActivityPage
{
    private ReceptionActivityPage(IReadOnlyList<ReceptionActivityItem> items, string? nextCursor, bool hasMore)
    {
        Items = items;
        NextCursor = nextCursor;
        HasMore = hasMore;
    }

    public IReadOnlyList<ReceptionActivityItem> Items { get; }
    public string? NextCursor { get; }
    public bool HasMore { get; }

    public static ReceptionActivityPage Create(IEnumerable<ReceptionActivityItem> items, string? nextCursor, bool hasMore)
    {
        ArgumentNullException.ThrowIfNull(items);
        var snapshot = items.ToArray();
        if (snapshot.Any(item => item is null || item.AuditEntryId == Guid.Empty || item.SourceEntityId == Guid.Empty || item.ClientId == Guid.Empty || string.IsNullOrWhiteSpace(item.ClientDisplayName)))
        {
            throw new ArgumentException("Activity rows must contain canonical audit, source and client references.", nameof(items));
        }

        if (hasMore != !string.IsNullOrWhiteSpace(nextCursor))
        {
            throw new ArgumentException("A next cursor is required exactly when more activity exists.", nameof(nextCursor));
        }

        return new ReceptionActivityPage(Array.AsReadOnly(snapshot), nextCursor, hasMore);
    }
}

public sealed record GetReceptionActivityResult(
    GetReceptionActivityStatus Status,
    ReceptionActivityPage? Page,
    string? ErrorCode,
    string? ErrorMessage,
    string? ErrorField)
{
    public static GetReceptionActivityResult Succeeded(ReceptionActivityPage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        return new(GetReceptionActivityStatus.Success, page, null, null, null);
    }

    public static GetReceptionActivityResult PermissionDenied() => Failure(GetReceptionActivityStatus.PermissionDenied, "permission_denied", "An active Owner, named Admin or shared Reception/Admin session is required.", null);
    public static GetReceptionActivityResult Invalid(string message, string field) => Failure(GetReceptionActivityStatus.ValidationFailed, "validation_failed", message, field);
    public static GetReceptionActivityResult RecalculationFailed() => Failure(GetReceptionActivityStatus.RecalculationFailed, "recalculation_failed", "Membership state is unavailable because recalculation has not completed successfully.", null);
    public static GetReceptionActivityResult SourceInconsistent() => Failure(GetReceptionActivityStatus.SourceInconsistent, "source_inconsistent", "Reception activity source records are inconsistent.", null);

    private static GetReceptionActivityResult Failure(GetReceptionActivityStatus status, string code, string message, string? field) => new(status, null, code, message, field);
}
