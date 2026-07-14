using BodyLife.Crm.Modules.Memberships;
using BodyLife.Crm.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BodyLife.Crm.Infrastructure.Persistence.Memberships;

public sealed class MembershipVisitEligibilityPreparer(
    BodyLifeDbContext dbContext,
    MembershipStateCacheRebuilder stateCacheRebuilder)
{
    public async Task<MembershipVisitEligibilityPreparationResult> PrepareAsync(
        Guid clientId,
        Guid membershipId,
        DateTimeOffset occurredAt,
        IEnumerable<MembershipVisitFreezeSource>? freezeSources,
        CancellationToken cancellationToken = default)
    {
        if (clientId == Guid.Empty)
        {
            throw new ArgumentException("Client id is required.", nameof(clientId));
        }

        if (membershipId == Guid.Empty)
        {
            throw new ArgumentException(
                "Membership id is required.",
                nameof(membershipId));
        }

        ArgumentNullException.ThrowIfNull(freezeSources);
        var explicitFreezeSources = freezeSources.ToArray();

        if (dbContext.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException(
                "Membership Visit eligibility preparation requires a caller-owned "
                + "database transaction so the selected Membership remains locked.");
        }

        var sourceRows = await dbContext.Set<IssuedMembershipRecord>()
            .FromSqlInterpolated(
                $"""
                select
                    id,
                    client_id,
                    membership_type_id,
                    type_name_snapshot,
                    duration_days_snapshot,
                    visits_limit_snapshot,
                    price_amount_snapshot,
                    price_currency_snapshot,
                    start_date,
                    base_end_date,
                    issued_at,
                    issued_by_account_id,
                    status,
                    entry_origin,
                    entry_batch_id,
                    comment
                from bodylife.issued_memberships
                where id = {membershipId}
                    and client_id = {clientId}
                for update
                """)
            .ToArrayAsync(cancellationToken);
        var source = sourceRows.SingleOrDefault();

        if (source is null)
        {
            return MembershipVisitEligibilityPreparationResult.NotFound(
                clientId,
                membershipId);
        }

        var rebuild = await stateCacheRebuilder.RebuildAsync(
            membershipId,
            cancellationToken);
        if (!rebuild.Succeeded || rebuild.State is null)
        {
            throw new InvalidOperationException(
                "The locked Membership source disappeared during state rebuild.");
        }

        var issueTerms = MembershipIssueTerms.FromIssuedSnapshot(
            source.MembershipTypeId,
            new IssuedMembershipSnapshot(
                source.TypeNameSnapshot,
                source.DurationDaysSnapshot,
                source.VisitsLimitSnapshot,
                new Money(source.PriceAmountSnapshot, source.PriceCurrencySnapshot)),
            source.StartDate,
            source.BaseEndDate);
        var lifecycleStatus = source.Status switch
        {
            "active" => IssuedMembershipLifecycleStatus.Active,
            "canceled" => IssuedMembershipLifecycleStatus.Canceled,
            "corrected" => IssuedMembershipLifecycleStatus.Corrected,
            _ => throw new InvalidOperationException(
                $"Issued Membership status '{source.Status}' is not supported."),
        };
        var eligibility = MembershipVisitEligibilityPolicy.Evaluate(
            membershipId,
            issueTerms,
            rebuild.State,
            lifecycleStatus,
            DateOnly.FromDateTime(occurredAt.DateTime),
            explicitFreezeSources);

        return MembershipVisitEligibilityPreparationResult.Prepared(
            clientId,
            membershipId,
            eligibility,
            rebuild.Status);
    }
}
