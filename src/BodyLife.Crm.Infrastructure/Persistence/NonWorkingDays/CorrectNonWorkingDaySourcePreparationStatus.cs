namespace BodyLife.Crm.Infrastructure.Persistence.NonWorkingDays;

public enum CorrectNonWorkingDaySourcePreparationStatus
{
    Prepared = 1,
    NotFound,
    AlreadyCanceled,
    AlreadyCorrected,
    InconsistentSource,
}
