using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Modules.MembershipTypes;

public sealed record MembershipTypeCatalogValues(
    string Name,
    int DurationDays,
    int VisitsLimit,
    Money Price,
    string? Comment);
