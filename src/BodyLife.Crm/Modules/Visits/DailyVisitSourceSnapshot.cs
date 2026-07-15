namespace BodyLife.Crm.Modules.Visits;

public sealed record DailyVisitSourceSnapshot(
    DateOnly BusinessDate,
    VisitDayReconciliationStatus DayStatus,
    IReadOnlyList<DailyVisitSourceRow> Rows)
{
    public int ActiveVisitCount => Rows.Count(row =>
        row.Visit.Status == ClientVisitRowStatus.Active);
}
