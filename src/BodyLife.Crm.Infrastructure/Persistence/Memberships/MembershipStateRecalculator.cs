using BodyLife.Crm.Modules.Memberships;
using Npgsql;

namespace BodyLife.Crm.Infrastructure.Persistence.Memberships;

public sealed class MembershipStateRecalculator(
    MembershipStateCacheRebuilder stateCacheRebuilder)
    : IMembershipStateRecalculator
{
    public async Task<MembershipStateRecalculationResult> RecalculateAsync(
        Guid membershipId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var rebuild = await stateCacheRebuilder.RebuildAsync(
                membershipId,
                cancellationToken);
            var status = rebuild.Succeeded
                ? MembershipStateRecalculationStatus.Recalculated
                : MembershipStateRecalculationStatus.MissingSource;

            return new MembershipStateRecalculationResult(membershipId, status);
        }
        catch (ArgumentException exception) when (!ContainsPostgresException(exception))
        {
            return new MembershipStateRecalculationResult(
                membershipId,
                MembershipStateRecalculationStatus.InvalidSourceState);
        }
        catch (InvalidOperationException exception)
            when (!ContainsPostgresException(exception))
        {
            return new MembershipStateRecalculationResult(
                membershipId,
                MembershipStateRecalculationStatus.InvalidSourceState);
        }
    }

    private static bool ContainsPostgresException(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException)
            {
                return true;
            }
        }

        return false;
    }
}
