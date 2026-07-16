using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.Modules.Payments;
using BodyLife.Crm.Modules.Visits;

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
    ClientVisitRowsPage? RecentVisits,
    ClientPaymentRowsPage? RecentPayments,
    IReadOnlyList<ClientWarning> Warnings,
    QueryPermissionSet AllowedActions);
