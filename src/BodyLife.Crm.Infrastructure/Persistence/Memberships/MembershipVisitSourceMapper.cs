using BodyLife.Crm.Modules.Memberships;
using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Infrastructure.Persistence.Memberships;

internal static class MembershipVisitSourceMapper
{
    public static MembershipVisitSourceFact Map(
        Guid membershipId,
        IEnumerable<MembershipVisitSourceRow> sourceRows)
    {
        var sources = sourceRows.ToArray();
        if (sources.Length == 0)
        {
            throw new ArgumentException(
                "Membership Visit source rows are required.",
                nameof(sourceRows));
        }

        var visitStatus = sources[0].VisitStatus switch
        {
            "active" => MembershipVisitSourceStatus.Active,
            "canceled" => MembershipVisitSourceStatus.Canceled,
            _ => throw new InvalidOperationException(
                $"Visit status '{sources[0].VisitStatus}' is not supported."),
        };
        var activeConsumptionSources = new List<MembershipVisitSourceRow>();
        foreach (var source in sources)
        {
            switch (source.ConsumptionStatus)
            {
                case "active":
                    activeConsumptionSources.Add(source);
                    break;
                case "canceled":
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Visit consumption status '{source.ConsumptionStatus}' "
                        + "is not supported.");
            }
        }

        if (activeConsumptionSources.Count > 1)
        {
            throw new InvalidOperationException(
                $"Visit '{sources[0].VisitId}' has multiple active counted consumptions.");
        }

        var effectiveSource = activeConsumptionSources.SingleOrDefault()
            ?? sources
                .OrderByDescending(source => source.ConsumptionRecordedAt)
                .ThenByDescending(source => source.ConsumptionId)
                .First();
        var status = visitStatus == MembershipVisitSourceStatus.Active
            && activeConsumptionSources.Count == 1
                ? MembershipVisitSourceStatus.Active
                : MembershipVisitSourceStatus.Canceled;

        return new MembershipVisitSourceFact(
            membershipId,
            effectiveSource.VisitId,
            BusinessTimeZone.GetBusinessDate(effectiveSource.OccurredAt),
            effectiveSource.OccurredAt,
            effectiveSource.ConsumptionRecordedAt,
            status);
    }
}

internal sealed record MembershipVisitSourceRow(
    Guid ConsumptionId,
    Guid VisitId,
    DateTimeOffset OccurredAt,
    string VisitStatus,
    DateTimeOffset ConsumptionRecordedAt,
    string ConsumptionStatus);
