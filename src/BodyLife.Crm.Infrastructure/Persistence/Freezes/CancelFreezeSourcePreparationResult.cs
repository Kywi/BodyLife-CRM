using BodyLife.Crm.Modules.Freezes;

namespace BodyLife.Crm.Infrastructure.Persistence.Freezes;

public sealed class CancelFreezeSourcePreparationResult
{
    private CancelFreezeSourcePreparationResult(
        CancelFreezeSourcePreparationStatus status,
        Guid freezeId,
        FreezeCancellationSource? source)
    {
        Status = status;
        FreezeId = freezeId;
        Source = source;
    }

    public CancelFreezeSourcePreparationStatus Status { get; }

    public Guid FreezeId { get; }

    public FreezeCancellationSource? Source { get; }

    public bool IsPrepared => Status == CancelFreezeSourcePreparationStatus.Prepared;

    internal static CancelFreezeSourcePreparationResult Prepared(
        FreezeCancellationSource source)
    {
        return new CancelFreezeSourcePreparationResult(
            CancelFreezeSourcePreparationStatus.Prepared,
            source.FreezeId,
            source);
    }

    internal static CancelFreezeSourcePreparationResult NotFound(Guid freezeId)
    {
        return new CancelFreezeSourcePreparationResult(
            CancelFreezeSourcePreparationStatus.NotFound,
            freezeId,
            source: null);
    }

    internal static CancelFreezeSourcePreparationResult AlreadyCanceled(
        FreezeCancellationSource source)
    {
        return new CancelFreezeSourcePreparationResult(
            CancelFreezeSourcePreparationStatus.AlreadyCanceled,
            source.FreezeId,
            source);
    }

    internal static CancelFreezeSourcePreparationResult InconsistentSource(
        Guid freezeId,
        FreezeCancellationSource? source = null)
    {
        return new CancelFreezeSourcePreparationResult(
            CancelFreezeSourcePreparationStatus.InconsistentSource,
            freezeId,
            source);
    }
}
