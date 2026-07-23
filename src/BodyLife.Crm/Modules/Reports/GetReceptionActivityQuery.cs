using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Modules.Reports;

public sealed record GetReceptionActivityQuery(
    ActorContext Actor,
    DateOnly RecordedBusinessDate,
    int Limit = 10,
    string? Cursor = null) : IBodyLifeQuery<GetReceptionActivityResult>
{
    public const int DefaultLimit = 10;
    public const int MaxLimit = 20;
}
