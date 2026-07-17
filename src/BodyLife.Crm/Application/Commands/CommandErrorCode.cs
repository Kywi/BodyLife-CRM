namespace BodyLife.Crm.Application.Commands;

public enum CommandErrorCode
{
    PermissionDenied = 1,
    ValidationFailed,
    NotFound,
    DuplicateSubmission,
    StaleState,
    CardNumberAlreadyCurrent,
    DuplicateWarningNotAcknowledged,
    DayClosedRequiresOwner,
    MembershipNotEligible,
    MembershipTypeInactive,
    AlreadyCanceled,
    RecalculationFailed,
    ConcurrencyConflict,
    AlreadyInactive,
    NegativeDecisionRequired,
    WarningAcknowledgementRequired,
    VisitDuringFreeze,
    ReasonRequired,
    FreezeConflictsWithVisit,
    PreviewExpired,
    AffectedScopeChanged
}
