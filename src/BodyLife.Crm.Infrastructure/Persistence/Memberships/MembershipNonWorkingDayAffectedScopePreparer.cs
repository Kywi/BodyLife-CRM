using System.Data;
using BodyLife.Crm.Modules.Memberships;
using BodyLife.Crm.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace BodyLife.Crm.Infrastructure.Persistence.Memberships;

public sealed class MembershipNonWorkingDayAffectedScopePreparer(
    BodyLifeDbContext dbContext,
    MembershipStateCacheRebuilder stateCacheRebuilder)
    : IMembershipNonWorkingDayAffectedScopePreparer,
      IMembershipNonWorkingDayImpactPreparer
{
    public async Task<MembershipNonWorkingDayAffectedScope> PrepareAsync(
        DateRange period,
        CancellationToken cancellationToken = default)
    {
        var impact = await PrepareImpactAsync(period, cancellationToken);
        return impact.AffectedScope;
    }

    public async Task<MembershipNonWorkingDayImpactPreparation> PrepareImpactAsync(
        DateRange period,
        CancellationToken cancellationToken = default)
    {
        var transaction = dbContext.Database.CurrentTransaction
            ?? throw new InvalidOperationException(
                "NonWorkingDay affected scope preparation requires a caller-owned "
                + "consistent database transaction.");
        var isolationLevel = transaction.GetDbTransaction().IsolationLevel;
        if (isolationLevel is not IsolationLevel.RepeatableRead
            and not IsolationLevel.Serializable)
        {
            throw new InvalidOperationException(
                "NonWorkingDay affected scope preparation requires RepeatableRead "
                + "or Serializable transaction isolation.");
        }

        var candidates = await dbContext.Set<IssuedMembershipRecord>()
            .FromSqlRaw(
                """
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
                where status = 'active'
                order by id
                for update
                """)
            .AsNoTracking()
            .ToArrayAsync(cancellationToken);
        var affectedMemberships = new List<MembershipNonWorkingDayAffectedScopeItem>();
        var impactItems = new List<MembershipNonWorkingDayImpactItem>();

        foreach (var candidate in candidates)
        {
            var canonical = await stateCacheRebuilder
                .CalculateCanonicalStateAfterMembershipLockAsync(
                    candidate,
                    cancellationToken);
            var application = MembershipNonWorkingDayApplicationPolicy.Evaluate(
                candidate.Id,
                canonical.IssueTerms,
                canonical.State,
                IssuedMembershipLifecycleStatus.Active,
                period);
            if (!application.IsEligible)
            {
                continue;
            }

            var appliedRange = application.AppliedRange
                ?? throw new InvalidOperationException(
                    "Eligible NonWorkingDay application did not preserve its full range.");
            affectedMemberships.Add(new MembershipNonWorkingDayAffectedScopeItem(
                candidate.Id,
                candidate.ClientId,
                appliedRange));
            impactItems.Add(new MembershipNonWorkingDayImpactItem(
                candidate.Id,
                candidate.ClientId,
                appliedRange,
                MembershipNonWorkingDayImpactEstimator.Estimate(
                    canonical.State,
                    canonical.ExtensionCalculation,
                    period)));
        }

        var affectedScope = new MembershipNonWorkingDayAffectedScope(
            period,
            affectedMemberships);
        return new MembershipNonWorkingDayImpactPreparation(
            affectedScope,
            impactItems);
    }
}
