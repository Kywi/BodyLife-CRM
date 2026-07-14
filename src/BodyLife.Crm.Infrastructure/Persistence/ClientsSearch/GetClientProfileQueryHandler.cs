using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.Modules.Clients.Search;
using BodyLife.Crm.Modules.Memberships;
using Microsoft.EntityFrameworkCore;

namespace BodyLife.Crm.Infrastructure.Persistence.ClientsSearch;

public sealed class GetClientProfileQueryHandler(
    BodyLifeDbContext dbContext,
    IBodyLifeQueryHandler<GetClientMembershipStatesQuery, GetClientMembershipStatesResult>
        getClientMembershipStates,
    TimeProvider timeProvider)
    : IBodyLifeQueryHandler<GetClientProfileQuery, GetClientProfileResult>
{
    private static readonly QueryPermissionSet ImplementedActionPermissions = new(
    [
        QueryPermissionResult.Allowed(
            ClientProfileActionKeys.UpdateClient,
            ClientProfileActionKeys.AdminOrOwnerPolicy),
        QueryPermissionResult.Allowed(
            ClientProfileActionKeys.AssignOrChangeCard,
            ClientProfileActionKeys.AdminOrOwnerPolicy),
    ]);

    public async Task<GetClientProfileResult> ExecuteAsync(
        GetClientProfileQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var requestTime = timeProvider.GetUtcNow();

        if (!await ClientQuerySupport.IsActorAuthorizedAsync(
                dbContext,
                query.Actor,
                requestTime,
                cancellationToken))
        {
            return GetClientProfileResult.Denied();
        }

        if (query.ClientId == Guid.Empty)
        {
            return GetClientProfileResult.Invalid("Client id is required.", "clientId");
        }

        if (query.MembershipAsOfDate is { } requestedAsOfDate
            && requestedAsOfDate == default)
        {
            return GetClientProfileResult.Invalid(
                "Membership as-of date is required when supplied.",
                "membershipAsOfDate");
        }

        if (query.IncludeHistory)
        {
            return GetClientProfileResult.Invalid(
                "Client history composition is not available in the profile shell.",
                "includeHistory");
        }

        if (query.IncludeDrillDowns)
        {
            return GetClientProfileResult.Invalid(
                "Client drill-down composition is not available in the profile shell.",
                "includeDrillDowns");
        }

        var currentCards = dbContext.Set<ClientCardAssignmentRecord>()
            .AsNoTracking()
            .Where(assignment => assignment.IsCurrent);
        var row = await (
            from client in dbContext.Set<ClientRecord>().AsNoTracking()
            join card in currentCards on client.Id equals card.ClientId into currentCardGroup
            from cardRecord in currentCardGroup.DefaultIfEmpty()
            where client.Id == query.ClientId
            select new
            {
                client.Id,
                client.Surname,
                client.Name,
                client.Patronymic,
                Phone = client.PhoneRaw,
                client.Comment,
                client.OperationalStatus,
                client.CreatedAt,
                client.UpdatedAt,
                CurrentCardAssignmentId = cardRecord == null ? (Guid?)null : cardRecord.Id,
                CurrentCardNumber = cardRecord == null ? null : cardRecord.CardNumberRaw,
                CurrentCardAssignedAt = cardRecord == null ? (DateTimeOffset?)null : cardRecord.AssignedAt,
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (row is null)
        {
            return GetClientProfileResult.Missing();
        }

        var membershipAsOfDate = query.MembershipAsOfDate
            ?? DateOnly.FromDateTime(requestTime.UtcDateTime);
        var membershipResult = await getClientMembershipStates.ExecuteAsync(
            new GetClientMembershipStatesQuery(
                query.Actor,
                query.ClientId,
                membershipAsOfDate),
            cancellationToken);

        if (membershipResult.Status != GetClientMembershipStatesStatus.Success)
        {
            return MapMembershipFailure(membershipResult);
        }

        if (membershipResult.StateCollection is not { } membershipStates
            || membershipStates.ClientId != query.ClientId
            || membershipStates.AsOfDate != membershipAsOfDate)
        {
            return GetClientProfileResult.RecalculationFailed();
        }

        var currentCard = row.CurrentCardAssignmentId.HasValue
            ? new ClientProfileCard(
                row.CurrentCardAssignmentId.Value,
                row.CurrentCardNumber!,
                row.CurrentCardAssignedAt!.Value)
            : null;
        var profile = new ClientProfile(
            row.Id,
            row.Surname,
            row.Name,
            row.Patronymic,
            ClientQuerySupport.BuildDisplayName(row.Surname, row.Name, row.Patronymic),
            row.Phone,
            row.Comment,
            ClientQuerySupport.MapOperationalStatus(row.OperationalStatus),
            row.CreatedAt,
            row.UpdatedAt,
            currentCard,
            membershipAsOfDate,
            ClientProfileMembershipProjection.Project(membershipStates),
            ClientQuerySupport.BuildWarnings(row.OperationalStatus, row.CurrentCardNumber),
            MergePermissions(membershipResult.AllowedActions));

        return GetClientProfileResult.Succeeded(profile);
    }

    private static GetClientProfileResult MapMembershipFailure(
        GetClientMembershipStatesResult membershipResult)
    {
        return membershipResult.Status switch
        {
            GetClientMembershipStatesStatus.PermissionDenied
                => GetClientProfileResult.Denied(),
            GetClientMembershipStatesStatus.NotFound
                => GetClientProfileResult.Missing(),
            GetClientMembershipStatesStatus.ValidationFailed
                => GetClientProfileResult.Invalid(
                    membershipResult.ErrorMessage ?? "Membership state request is invalid.",
                    membershipResult.ErrorField == "asOfDate"
                        ? "membershipAsOfDate"
                        : membershipResult.ErrorField),
            GetClientMembershipStatesStatus.RecalculationFailed
                => GetClientProfileResult.RecalculationFailed(),
            _ => GetClientProfileResult.RecalculationFailed(),
        };
    }

    private static QueryPermissionSet MergePermissions(
        QueryPermissionSet membershipPermissions)
    {
        return new QueryPermissionSet(
            ImplementedActionPermissions.Items.Concat(membershipPermissions.Items));
    }
}
