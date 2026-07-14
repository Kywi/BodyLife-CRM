namespace BodyLife.Crm.Infrastructure.Persistence.Visits;

public enum CancelVisitSourcePreparationStatus
{
    Prepared = 1,
    NotFound,
    AlreadyCanceled,
    InconsistentSource,
}
