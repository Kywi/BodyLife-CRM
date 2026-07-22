using BodyLife.Crm.Modules.Visits;
using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Modules.Reports;

public sealed class InactiveClientLastVisit
{
    public InactiveClientLastVisit(
        Guid visitId,
        DateTimeOffset occurredAt,
        VisitKind visitKind)
    {
        if (visitId == Guid.Empty)
        {
            throw new ArgumentException("Visit id is required.", nameof(visitId));
        }

        if (occurredAt == default)
        {
            throw new ArgumentException("Visit occurrence time is required.", nameof(occurredAt));
        }

        if (!Enum.IsDefined(visitKind))
        {
            throw new ArgumentOutOfRangeException(
                nameof(visitKind),
                visitKind,
                "Visit kind is not supported.");
        }

        VisitId = visitId;
        OccurredAt = occurredAt;
        VisitKind = visitKind;
    }

    public Guid VisitId { get; }

    public DateTimeOffset OccurredAt { get; }

    public VisitKind VisitKind { get; }

    public DateOnly BusinessDate => BusinessTimeZone.GetBusinessDate(OccurredAt);
}
