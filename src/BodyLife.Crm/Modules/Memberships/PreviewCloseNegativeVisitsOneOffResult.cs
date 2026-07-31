namespace BodyLife.Crm.Modules.Memberships;

public sealed record PreviewCloseNegativeVisitsOneOffResult(
    PreviewCloseNegativeVisitsOneOffStatus Status,
    OneOffNegativeClosurePreview? Preview,
    NegativeVisitCoverageStaleSelectors? CurrentSelectors,
    string? ErrorCode,
    string? ErrorMessage,
    string? ErrorField)
{
    public static PreviewCloseNegativeVisitsOneOffResult Succeeded(
        OneOffNegativeClosurePreview preview) =>
        new(
            PreviewCloseNegativeVisitsOneOffStatus.Success,
            preview,
            preview.CurrentSelectors,
            null,
            null,
            null);

    public static PreviewCloseNegativeVisitsOneOffResult Failure(
        PreviewCloseNegativeVisitsOneOffStatus status,
        string errorCode,
        string errorMessage,
        string? errorField,
        NegativeVisitCoverageStaleSelectors? currentSelectors = null) =>
        new(status, null, currentSelectors, errorCode, errorMessage, errorField);
}
