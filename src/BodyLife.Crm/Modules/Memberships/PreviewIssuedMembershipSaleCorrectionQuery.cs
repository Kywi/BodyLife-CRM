using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Modules.Memberships;

/// <summary>
/// Read-only, advisory correction preview. The dependency token must be supplied
/// unchanged to a subsequent cancellation or replacement command.
/// </summary>
public sealed record PreviewIssuedMembershipSaleCorrectionQuery(
    ActorContext Actor,
    Guid OriginalMembershipId,
    Guid? ReplacementMembershipTypeId = null,
    DateOnly? ReplacementStartDate = null)
    : IBodyLifeQuery<PreviewIssuedMembershipSaleCorrectionResult>;
