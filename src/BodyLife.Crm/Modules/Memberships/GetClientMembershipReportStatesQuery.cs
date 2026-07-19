using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Modules.Memberships;

public sealed record GetClientMembershipReportStatesQuery(
    ActorContext Actor,
    DateOnly AsOfDate,
    IReadOnlyList<Guid> ClientIds)
    : IBodyLifeQuery<GetClientMembershipReportStatesResult>
{
    public const int MaxClientCount = 100;
}
