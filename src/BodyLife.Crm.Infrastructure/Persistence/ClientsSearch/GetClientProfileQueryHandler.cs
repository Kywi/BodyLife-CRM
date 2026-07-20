using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.Modules.Clients.Search;
using BodyLife.Crm.Modules.Freezes;
using BodyLife.Crm.Modules.Memberships;
using BodyLife.Crm.Modules.Payments;
using BodyLife.Crm.Modules.Visits;
using Microsoft.EntityFrameworkCore;

namespace BodyLife.Crm.Infrastructure.Persistence.ClientsSearch;

public sealed class GetClientProfileQueryHandler(
    BodyLifeDbContext dbContext,
    IBodyLifeQueryHandler<GetClientMembershipStatesQuery, GetClientMembershipStatesResult>
        getClientMembershipStates,
    IBodyLifeQueryHandler<
        GetClientMembershipExtensionExplanationsQuery,
        GetClientMembershipExtensionExplanationsResult>
        getClientMembershipExtensionExplanations,
    IBodyLifeQueryHandler<GetClientVisitRowsQuery, GetClientVisitRowsResult>
        getClientVisitRows,
    IBodyLifeQueryHandler<GetClientPaymentRowsQuery, GetClientPaymentRowsResult>
        getClientPaymentRows,
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
        QueryPermissionResult.Allowed(
            PaymentActionKeys.Create,
            PaymentActionKeys.AdminOrOwnerPolicy),
        QueryPermissionResult.Allowed(
            FreezeActionKeys.Add,
            FreezeActionKeys.AdminOrOwnerPolicy),
        QueryPermissionResult.Allowed(
            FreezeActionKeys.Cancel,
            FreezeActionKeys.AdminOrOwnerPolicy),
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

        if (query.RequiredPaymentId == Guid.Empty)
        {
            return GetClientProfileResult.Invalid(
                "Required Payment id must not be empty when supplied.",
                "requiredPaymentId");
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

        var extensionExplanationsResult =
            await getClientMembershipExtensionExplanations.ExecuteAsync(
                new GetClientMembershipExtensionExplanationsQuery(
                    query.Actor,
                    query.ClientId,
                    IncludeInactiveSources: query.IncludeHistory),
                cancellationToken);
        if (extensionExplanationsResult.Status
            != GetClientMembershipExtensionExplanationsStatus.Success)
        {
            return MapExtensionExplanationsFailure(extensionExplanationsResult);
        }

        if (extensionExplanationsResult.Explanations is not { } extensionExplanations
            || extensionExplanations.ClientId != query.ClientId)
        {
            return GetClientProfileResult.InconsistentSource();
        }

        var membershipIds = membershipStates.Timeline
            .Select(item => item.State.MembershipId)
            .ToHashSet();
        var explanationIdentities = extensionExplanations.Items
            .Select(explanation => (
                explanation.MembershipId,
                explanation.SourceKind,
                explanation.SourceId))
            .ToArray();
        if (explanationIdentities.Any(identity => !membershipIds.Contains(identity.MembershipId))
            || explanationIdentities.Distinct().Count() != explanationIdentities.Length)
        {
            return GetClientProfileResult.InconsistentSource();
        }

        ClientVisitRowsPage? recentVisits = null;
        ClientPaymentRowsPage? recentPayments = null;
        if (query.IncludeHistory)
        {
            var visitRowsResult = await getClientVisitRows.ExecuteAsync(
                new GetClientVisitRowsQuery(query.Actor, query.ClientId),
                cancellationToken);
            if (visitRowsResult.Status != GetClientVisitRowsStatus.Success)
            {
                return MapVisitRowsFailure(visitRowsResult);
            }

            if (visitRowsResult.Page is not { } visitRows
                || visitRows.ClientId != query.ClientId)
            {
                return GetClientProfileResult.InconsistentSource();
            }

            recentVisits = visitRows;

            var paymentRowsResult = await getClientPaymentRows.ExecuteAsync(
                new GetClientPaymentRowsQuery(
                    query.Actor,
                    query.ClientId,
                    RequiredPaymentId: query.RequiredPaymentId),
                cancellationToken);
            if (paymentRowsResult.Status != GetClientPaymentRowsStatus.Success)
            {
                return MapPaymentRowsFailure(paymentRowsResult);
            }

            if (paymentRowsResult.Page is not { } paymentRows
                || paymentRows.ClientId != query.ClientId)
            {
                return GetClientProfileResult.InconsistentSource();
            }

            recentPayments = paymentRows;
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
            ClientProfileMembershipProjection.Project(
                membershipStates,
                extensionExplanations.Items),
            recentVisits,
            recentPayments,
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

    private static GetClientProfileResult MapVisitRowsFailure(
        GetClientVisitRowsResult visitRowsResult)
    {
        return visitRowsResult.Status switch
        {
            GetClientVisitRowsStatus.PermissionDenied
                => GetClientProfileResult.Denied(),
            GetClientVisitRowsStatus.NotFound
                => GetClientProfileResult.Missing(),
            GetClientVisitRowsStatus.ValidationFailed
                => GetClientProfileResult.Invalid(
                    visitRowsResult.ErrorMessage ?? "Visit history request is invalid.",
                    visitRowsResult.ErrorField),
            GetClientVisitRowsStatus.SourceInconsistent
                => GetClientProfileResult.InconsistentSource(),
            _ => GetClientProfileResult.InconsistentSource(),
        };
    }

    private static GetClientProfileResult MapExtensionExplanationsFailure(
        GetClientMembershipExtensionExplanationsResult extensionExplanationsResult)
    {
        return extensionExplanationsResult.Status switch
        {
            GetClientMembershipExtensionExplanationsStatus.PermissionDenied
                => GetClientProfileResult.Denied(),
            GetClientMembershipExtensionExplanationsStatus.NotFound
                => GetClientProfileResult.Missing(),
            GetClientMembershipExtensionExplanationsStatus.ValidationFailed
                => GetClientProfileResult.Invalid(
                    extensionExplanationsResult.ErrorMessage
                        ?? "Membership extension explanation request is invalid.",
                    extensionExplanationsResult.ErrorField),
            GetClientMembershipExtensionExplanationsStatus.SourceInconsistent
                => GetClientProfileResult.InconsistentSource(),
            _ => GetClientProfileResult.InconsistentSource(),
        };
    }

    private static GetClientProfileResult MapPaymentRowsFailure(
        GetClientPaymentRowsResult paymentRowsResult)
    {
        return paymentRowsResult.Status switch
        {
            GetClientPaymentRowsStatus.PermissionDenied
                => GetClientProfileResult.Denied(),
            GetClientPaymentRowsStatus.NotFound
                => GetClientProfileResult.Missing(),
            GetClientPaymentRowsStatus.ValidationFailed
                => GetClientProfileResult.Invalid(
                    paymentRowsResult.ErrorMessage
                        ?? "Payment history request is invalid.",
                    paymentRowsResult.ErrorField),
            GetClientPaymentRowsStatus.SourceInconsistent
                => GetClientProfileResult.InconsistentSource(),
            _ => GetClientProfileResult.InconsistentSource(),
        };
    }

    private static QueryPermissionSet MergePermissions(
        QueryPermissionSet membershipPermissions)
    {
        return new QueryPermissionSet(
            ImplementedActionPermissions.Items.Concat(membershipPermissions.Items));
    }
}
