using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Modules.Memberships;

public sealed record GetReceptionAttentionCountsQuery(
    ActorContext Actor,
    DateOnly AsOfDate,
    int EndingSoonDaysThreshold)
    : IBodyLifeQuery<GetReceptionAttentionCountsResult>;
