using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Modules.Memberships;

public sealed record PreviewCloseNegativeVisitsOneOffQuery(
    ActorContext Actor,
    Guid ClientId,
    Guid ExpectedOldestOpenNegativeVisitId,
    IReadOnlyList<NegativeVisitClosureLineSelection>? Lines)
    : IBodyLifeQuery<PreviewCloseNegativeVisitsOneOffResult>;
