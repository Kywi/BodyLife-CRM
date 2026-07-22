using System.Data;
using BodyLife.Crm.Modules.Memberships;
using BodyLife.Crm.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace BodyLife.Crm.Infrastructure.Persistence.Memberships;

public sealed class MembershipFreezeEligibilityPreparer
{
    private const string VisitSourcesWithUpperBoundSql =
        """
        select
            consumption.id,
            consumption.visit_id,
            visit.occurred_at,
            visit.status as visit_status,
            consumption.recorded_at as consumption_recorded_at,
            consumption.status as consumption_status
        from bodylife.visit_consumptions as consumption
        inner join bodylife.visits as visit
            on visit.id = consumption.visit_id
            and visit.client_id = consumption.client_id
            and visit.visit_kind = consumption.visit_kind
        where consumption.membership_id = @membership_id
            and visit.occurred_at >= @range_start
            and visit.occurred_at < @range_end_exclusive
        order by
            visit.occurred_at,
            visit.id,
            consumption.recorded_at,
            consumption.id
        for update of visit, consumption
        """;

    private const string VisitSourcesWithoutUpperBoundSql =
        """
        select
            consumption.id,
            consumption.visit_id,
            visit.occurred_at,
            visit.status as visit_status,
            consumption.recorded_at as consumption_recorded_at,
            consumption.status as consumption_status
        from bodylife.visit_consumptions as consumption
        inner join bodylife.visits as visit
            on visit.id = consumption.visit_id
            and visit.client_id = consumption.client_id
            and visit.visit_kind = consumption.visit_kind
        where consumption.membership_id = @membership_id
            and visit.occurred_at >= @range_start
        order by
            visit.occurred_at,
            visit.id,
            consumption.recorded_at,
            consumption.id
        for update of visit, consumption
        """;

    private readonly BodyLifeDbContext dbContext;
    private readonly MembershipStateCacheRebuilder stateCacheRebuilder;

    public MembershipFreezeEligibilityPreparer(
        BodyLifeDbContext dbContext,
        MembershipStateCacheRebuilder stateCacheRebuilder)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(stateCacheRebuilder);

        this.dbContext = dbContext;
        this.stateCacheRebuilder = stateCacheRebuilder;
    }

    public async Task<MembershipFreezeEligibilityPreparationResult> PrepareAsync(
        Guid clientId,
        Guid membershipId,
        DateRange range,
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

        var transaction = dbContext.Database.CurrentTransaction
            ?? throw new InvalidOperationException(
                "Membership Freeze eligibility preparation requires a caller-owned "
                + "database transaction so the selected Membership and relevant "
                + "Visit sources remain locked.");

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
            return MembershipFreezeEligibilityPreparationResult.NotFound(
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

        var visitSources = await LoadVisitSourcesAsync(
            transaction,
            membershipId,
            range,
            cancellationToken);
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
        var eligibility = MembershipFreezeEligibilityPolicy.Evaluate(
            membershipId,
            issueTerms,
            rebuild.State,
            lifecycleStatus,
            range,
            visitSources);

        return MembershipFreezeEligibilityPreparationResult.Prepared(
            clientId,
            membershipId,
            eligibility,
            rebuild.Status);
    }

    private async Task<MembershipVisitSourceFact[]> LoadVisitSourcesAsync(
        IDbContextTransaction transaction,
        Guid membershipId,
        DateRange range,
        CancellationToken cancellationToken)
    {
        var rangeStart = BusinessTimeZone.GetUtcDayRange(range.StartDate).FromInclusive;
        var rangeEndExclusive = range.EndDate == DateOnly.MaxValue
            ? (DateTimeOffset?)null
            : BusinessTimeZone.GetUtcDayRange(range.EndDate).ToExclusive;
        await using var command = dbContext.Database.GetDbConnection().CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandText = rangeEndExclusive is null
            ? VisitSourcesWithoutUpperBoundSql
            : VisitSourcesWithUpperBoundSql;
        AddParameter(command, "membership_id", DbType.Guid, membershipId);
        AddParameter(command, "range_start", DbType.DateTimeOffset, rangeStart);
        if (rangeEndExclusive is not null)
        {
            AddParameter(
                command,
                "range_end_exclusive",
                DbType.DateTimeOffset,
                rangeEndExclusive.Value);
        }

        var rows = new List<MembershipVisitSourceRow>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new MembershipVisitSourceRow(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetFieldValue<DateTimeOffset>(2),
                reader.GetString(3),
                reader.GetFieldValue<DateTimeOffset>(4),
                reader.GetString(5)));
        }

        return rows
            .GroupBy(row => row.VisitId)
            .Select(sourceRows => MembershipVisitSourceMapper.Map(
                membershipId,
                sourceRows))
            .ToArray();
    }


    private static void AddParameter(
        System.Data.Common.DbCommand command,
        string name,
        DbType dbType,
        object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.DbType = dbType;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
