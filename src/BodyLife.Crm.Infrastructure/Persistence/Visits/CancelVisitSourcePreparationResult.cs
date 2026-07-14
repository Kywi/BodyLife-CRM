using BodyLife.Crm.Modules.Visits;

namespace BodyLife.Crm.Infrastructure.Persistence.Visits;

public sealed class CancelVisitSourcePreparationResult
{
    private CancelVisitSourcePreparationResult(
        CancelVisitSourcePreparationStatus status,
        Guid visitId,
        VisitCancellationSource? source)
    {
        Status = status;
        VisitId = visitId;
        Source = source;
    }

    public CancelVisitSourcePreparationStatus Status { get; }

    public Guid VisitId { get; }

    public VisitCancellationSource? Source { get; }

    public bool IsPrepared => Status == CancelVisitSourcePreparationStatus.Prepared;

    internal static CancelVisitSourcePreparationResult Prepared(
        VisitCancellationSource source)
    {
        return new CancelVisitSourcePreparationResult(
            CancelVisitSourcePreparationStatus.Prepared,
            source.VisitId,
            source);
    }

    internal static CancelVisitSourcePreparationResult NotFound(Guid visitId)
    {
        return new CancelVisitSourcePreparationResult(
            CancelVisitSourcePreparationStatus.NotFound,
            visitId,
            source: null);
    }

    internal static CancelVisitSourcePreparationResult AlreadyCanceled(
        VisitCancellationSource source)
    {
        return new CancelVisitSourcePreparationResult(
            CancelVisitSourcePreparationStatus.AlreadyCanceled,
            source.VisitId,
            source);
    }

    internal static CancelVisitSourcePreparationResult InconsistentSource(
        VisitCancellationSource source)
    {
        return new CancelVisitSourcePreparationResult(
            CancelVisitSourcePreparationStatus.InconsistentSource,
            source.VisitId,
            source);
    }
}
