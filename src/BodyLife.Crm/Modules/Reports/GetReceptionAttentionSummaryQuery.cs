using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Modules.Reports;

public sealed record GetReceptionAttentionSummaryQuery(ActorContext Actor, DateOnly AsOfDate, int EndingSoonDaysThreshold)
    : IBodyLifeQuery<GetReceptionAttentionSummaryResult>;
