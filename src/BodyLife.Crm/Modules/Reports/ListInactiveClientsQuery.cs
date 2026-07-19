using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Modules.Reports;

public sealed record ListInactiveClientsQuery(
    ActorContext Actor,
    DateOnly AsOfDate,
    int ThresholdDays,
    bool IncludeClientsWithNoVisits = false,
    int Limit = ListInactiveClientsQuery.DefaultLimit,
    int Offset = 0)
    : IBodyLifeQuery<ListInactiveClientsResult>
{
    public const int DefaultLimit = 50;
    public const int MaxLimit = 100;
    public const int MaxOffset = 10_000;

    public static bool IsSupportedThreshold(int thresholdDays)
    {
        return thresholdDays is 14 or 30 or 60;
    }
}
