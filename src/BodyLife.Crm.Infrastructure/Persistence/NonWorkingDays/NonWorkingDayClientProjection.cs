using BodyLife.Crm.Infrastructure.Persistence.ClientsSearch;
using BodyLife.Crm.Modules.Memberships;
using Microsoft.EntityFrameworkCore;

namespace BodyLife.Crm.Infrastructure.Persistence.NonWorkingDays;

internal static class NonWorkingDayClientProjection
{
    internal static async Task<IReadOnlyDictionary<Guid, string>> LoadDisplayNamesAsync(
        BodyLifeDbContext dbContext,
        MembershipNonWorkingDayImpactPreparation preparation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(preparation);

        var clientIds = preparation.AffectedMemberships
            .Select(item => item.ClientId)
            .Distinct()
            .ToArray();
        return await LoadDisplayNamesAsync(
            dbContext,
            clientIds,
            cancellationToken);
    }

    internal static async Task<IReadOnlyDictionary<Guid, string>> LoadDisplayNamesAsync(
        BodyLifeDbContext dbContext,
        IReadOnlyCollection<Guid> clientIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(clientIds);

        if (clientIds.Any(id => id == Guid.Empty)
            || clientIds.Distinct().Count() != clientIds.Count)
        {
            throw new ArgumentException(
                "Canonical Client ids must be non-empty and unique.",
                nameof(clientIds));
        }

        if (clientIds.Count == 0)
        {
            return new Dictionary<Guid, string>();
        }

        var clients = await dbContext.Set<ClientRecord>()
            .AsNoTracking()
            .Where(client => clientIds.Contains(client.Id))
            .Select(client => new
            {
                client.Id,
                client.Surname,
                client.Name,
                client.Patronymic,
            })
            .ToArrayAsync(cancellationToken);

        if (clients.Length != clientIds.Count)
        {
            throw new InvalidOperationException(
                "Every affected Membership must reference a canonical Client.");
        }

        return clients.ToDictionary(
            client => client.Id,
            client => ClientQuerySupport.BuildDisplayName(
                client.Surname,
                client.Name,
                client.Patronymic));
    }
}
