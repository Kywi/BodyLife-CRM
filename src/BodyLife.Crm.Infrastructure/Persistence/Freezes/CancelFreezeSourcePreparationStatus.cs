namespace BodyLife.Crm.Infrastructure.Persistence.Freezes;

public enum CancelFreezeSourcePreparationStatus
{
    Prepared = 1,
    NotFound,
    AlreadyCanceled,
    InconsistentSource,
}
