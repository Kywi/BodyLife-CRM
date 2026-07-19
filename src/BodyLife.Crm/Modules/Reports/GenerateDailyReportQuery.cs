using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Modules.Reports;

public sealed record GenerateDailyReportQuery(
    ActorContext Actor,
    DateOnly BusinessDate,
    bool IncludeDrillDown = true)
    : IBodyLifeQuery<GenerateDailyReportResult>;
