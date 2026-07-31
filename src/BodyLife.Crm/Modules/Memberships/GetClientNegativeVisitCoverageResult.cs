using BodyLife.Crm.Application.Queries;

namespace BodyLife.Crm.Modules.Memberships;

public sealed record GetClientNegativeVisitCoverageResult(
    GetClientNegativeVisitCoverageStatus Status,
    ClientNegativeVisitCoverageReadModel? Coverage,
    QueryPermissionSet AllowedActions,
    string? ErrorCode,
    string? ErrorMessage,
    string? ErrorField)
{
    public static GetClientNegativeVisitCoverageResult Succeeded(
        ClientNegativeVisitCoverageReadModel coverage,
        QueryPermissionSet allowedActions)
    {
        ArgumentNullException.ThrowIfNull(coverage);
        ArgumentNullException.ThrowIfNull(allowedActions);
        return new(GetClientNegativeVisitCoverageStatus.Success, coverage, allowedActions, null, null, null);
    }

    public static GetClientNegativeVisitCoverageResult Denied() => Failure(
        GetClientNegativeVisitCoverageStatus.PermissionDenied,
        "permission_denied",
        "An active Owner, named Admin or shared Reception/Admin session is required.",
        null);

    public static GetClientNegativeVisitCoverageResult MissingClient() => Failure(
        GetClientNegativeVisitCoverageStatus.NotFound,
        "not_found",
        "Client was not found.",
        "clientId");

    public static GetClientNegativeVisitCoverageResult Invalid(string message, string field) => Failure(
        GetClientNegativeVisitCoverageStatus.ValidationFailed,
        "validation_failed",
        message,
        field);

    public static GetClientNegativeVisitCoverageResult RecalculationFailed() => Failure(
        GetClientNegativeVisitCoverageStatus.RecalculationFailed,
        "recalculation_failed",
        "Canonical membership state is missing, stale or unavailable.",
        null);

    public static GetClientNegativeVisitCoverageResult CanonicalStateInvalid() => Failure(
        GetClientNegativeVisitCoverageStatus.CanonicalStateInvalid,
        "canonical_state_invalid",
        "Canonical negative-coverage facts are inconsistent.",
        null);

    private static GetClientNegativeVisitCoverageResult Failure(
        GetClientNegativeVisitCoverageStatus status,
        string errorCode,
        string errorMessage,
        string? field) => new(status, null, QueryPermissionSet.Empty, errorCode, errorMessage, field);
}
