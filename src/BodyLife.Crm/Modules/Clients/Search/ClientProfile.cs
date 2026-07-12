using BodyLife.Crm.Application.Queries;

namespace BodyLife.Crm.Modules.Clients.Search;

public sealed record ClientProfile(
    Guid ClientId,
    string Surname,
    string Name,
    string? Patronymic,
    string DisplayName,
    string? Phone,
    string? Comment,
    ClientOperationalStatus OperationalStatus,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    ClientProfileCard? CurrentCard,
    DateOnly? MembershipAsOfDate,
    ClientProfileMembershipArea Membership,
    IReadOnlyList<ClientWarning> Warnings,
    QueryPermissionSet AllowedActions);
