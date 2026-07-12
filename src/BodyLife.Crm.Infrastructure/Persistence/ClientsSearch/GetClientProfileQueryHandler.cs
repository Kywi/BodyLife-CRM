using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.Modules.Clients.Search;
using Microsoft.EntityFrameworkCore;

namespace BodyLife.Crm.Infrastructure.Persistence.ClientsSearch;

public sealed class GetClientProfileQueryHandler(
    BodyLifeDbContext dbContext,
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

        if (!await ClientQuerySupport.IsActorAuthorizedAsync(
                dbContext,
                query.Actor,
                timeProvider.GetUtcNow(),
                cancellationToken))
        {
            return GetClientProfileResult.Denied();
        }

        if (query.ClientId == Guid.Empty)
        {
            return GetClientProfileResult.Invalid("Client id is required.", "clientId");
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
            query.MembershipAsOfDate,
            ClientProfileMembershipArea.Empty,
            ClientQuerySupport.BuildWarnings(row.OperationalStatus, row.CurrentCardNumber),
            ImplementedActionPermissions);

        return GetClientProfileResult.Succeeded(profile);
    }
}
