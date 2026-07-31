namespace BodyLife.Crm.Modules.Memberships;

public sealed record PreviewCorrectNegativeVisitCoverageResult(
    PreviewCorrectNegativeVisitCoverageStatus Status,
    NegativeVisitCoverageCorrectionPreview? Preview,
    NegativeVisitCoverageStaleSelectors? CurrentSelectors,
    string? ErrorCode,
    string? ErrorMessage,
    string? ErrorField)
{
    public static PreviewCorrectNegativeVisitCoverageResult Succeeded(
        NegativeVisitCoverageCorrectionPreview preview) =>
        new(
            PreviewCorrectNegativeVisitCoverageStatus.Success,
            preview,
            preview.CurrentSelectors,
            null,
            null,
            null);

    public static PreviewCorrectNegativeVisitCoverageResult Failure(
        PreviewCorrectNegativeVisitCoverageStatus status,
        string errorCode,
        string errorMessage,
        string? errorField,
        NegativeVisitCoverageStaleSelectors? currentSelectors = null) =>
        new(status, null, currentSelectors, errorCode, errorMessage, errorField);
}
