using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Modules.MembershipTypes;

public sealed record MembershipTypeCatalogItem(
    Guid MembershipTypeId,
    string Name,
    int DurationDays,
    int VisitsLimit,
    Money Price,
    bool IsActive,
    string? Comment,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? DeactivatedAt)
{
    public bool IsAvailableForOrdinaryIssue => IsActive;
}
