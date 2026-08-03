using System.Security.Cryptography;
using System.Text.Json;
using BodyLife.Crm.Infrastructure.Persistence.Freezes;
using BodyLife.Crm.Infrastructure.Persistence.NonWorkingDays;
using BodyLife.Crm.Infrastructure.Persistence.Payments;
using BodyLife.Crm.Infrastructure.Persistence.Visits;
using BodyLife.Crm.Modules.Memberships;
using BodyLife.Crm.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BodyLife.Crm.Infrastructure.Persistence.Memberships;

internal static class IssuedMembershipSaleCorrectionSupport
{
    internal const string ActiveStatus = "active";
    internal const string SaleMode = "sale";
    internal const string SalePaymentContext = "membership_sale";

    internal static async Task<IssuedMembershipSaleSourceResult> ReadActiveSaleAsync(
        BodyLifeDbContext dbContext,
        Guid membershipId,
        CancellationToken cancellationToken)
    {
        var membership = await dbContext.Set<IssuedMembershipRecord>()
            .AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == membershipId, cancellationToken);
        if (membership is null)
        {
            return IssuedMembershipSaleSourceResult.Missing();
        }

        var payments = await dbContext.Set<PaymentRecord>()
            .AsNoTracking()
            .Where(payment =>
                payment.MembershipId == membershipId
                && payment.PaymentContext == SalePaymentContext)
            .ToArrayAsync(cancellationToken);
        var hasCorrection = await dbContext
            .Set<IssuedMembershipSaleCorrectionRecord>()
            .AsNoTracking()
            .AnyAsync(
                correction => correction.OriginalMembershipId == membershipId,
                cancellationToken);

        if (membership.Status != ActiveStatus
            || membership.IssuanceMode != SaleMode
            || hasCorrection)
        {
            return IssuedMembershipSaleSourceResult.Ineligible();
        }

        if (payments.Length != 1
            || !IsExactActiveSale(membership, payments[0]))
        {
            return IssuedMembershipSaleSourceResult.Inconsistent();
        }

        return IssuedMembershipSaleSourceResult.Prepared(membership, payments[0]);
    }

    internal static bool IsExactActiveSale(
        IssuedMembershipRecord membership,
        PaymentRecord payment)
    {
        return membership.Id != Guid.Empty
            && membership.ClientId != Guid.Empty
            && membership.MembershipTypeId != Guid.Empty
            && membership.IssuedByAccountId != Guid.Empty
            && membership.IssuanceMode == SaleMode
            && membership.Status == ActiveStatus
            && !string.IsNullOrWhiteSpace(membership.TypeNameSnapshot)
            && membership.DurationDaysSnapshot > 0
            && membership.VisitsLimitSnapshot >= 0
            && membership.PriceAmountSnapshot > 0
            && !string.IsNullOrWhiteSpace(membership.PriceCurrencySnapshot)
            && membership.PriceCurrencySnapshot
                == membership.PriceCurrencySnapshot.Trim().ToUpperInvariant()
            && MembershipDateRules.CalculateBaseEndDate(
                membership.StartDate,
                membership.DurationDaysSnapshot) == membership.BaseEndDate
            && payment.Id != Guid.Empty
            && payment.ClientId == membership.ClientId
            && payment.MembershipId == membership.Id
            && payment.NegativeClosureId is null
            && payment.Amount == membership.PriceAmountSnapshot
            && payment.Currency == membership.PriceCurrencySnapshot
            && payment.Method == "cash"
            && payment.PaymentContext == SalePaymentContext
            && payment.Status == ActiveStatus;
    }

    internal static async Task<IssuedMembershipSaleDependenciesResult>
        ReadDependenciesAsync(
            BodyLifeDbContext dbContext,
            Guid membershipId,
            Guid clientId,
            CancellationToken cancellationToken)
    {
        var consumptionRows = await (
            from consumption in dbContext.Set<VisitConsumptionRecord>().AsNoTracking()
            join visit in dbContext.Set<VisitRecord>().AsNoTracking()
                on consumption.VisitId equals visit.Id
            where consumption.MembershipId == membershipId
                && consumption.Status == ActiveStatus
            select new
            {
                Consumption = consumption,
                Visit = visit,
            }).ToArrayAsync(cancellationToken);

        var freezeRows = await dbContext.Set<FreezeRecord>()
            .AsNoTracking()
            .Where(freeze =>
                freeze.MembershipId == membershipId
                && freeze.Status == ActiveStatus)
            .ToArrayAsync(cancellationToken);

        var nonWorkingRows = await (
            from application in dbContext
                .Set<NonWorkingPeriodApplicationRecord>()
                .AsNoTracking()
            join period in dbContext.Set<NonWorkingPeriodRecord>().AsNoTracking()
                on application.NonWorkingPeriodId equals period.Id
            where application.MembershipId == membershipId
                && application.Status == ActiveStatus
            select new
            {
                Application = application,
                Period = period,
            }).ToArrayAsync(cancellationToken);

        var coverageRows = await (
            from item in dbContext.Set<MembershipNegativeClosureItemRecord>()
                .AsNoTracking()
            join closure in dbContext.Set<MembershipNegativeClosureRecord>()
                .AsNoTracking()
                on item.NegativeClosureId equals closure.Id
            where item.Status == ActiveStatus
                && (item.SourceMembershipId == membershipId
                    || item.CoveringMembershipId == membershipId)
            select new
            {
                Item = item,
                Closure = closure,
            }).ToArrayAsync(cancellationToken);

        if (consumptionRows.Any(row =>
                row.Consumption.ClientId != clientId
                || row.Visit.ClientId != clientId
                || row.Visit.Status != ActiveStatus
                || row.Consumption.VisitKind != "membership"
                || row.Visit.VisitKind != "membership"
                || row.Consumption.ConsumptionType
                    is not ("counted" or "negative_coverage"))
            || freezeRows.Any(freeze => freeze.ClientId != clientId)
            || nonWorkingRows.Any(row =>
                row.Application.ClientId != clientId
                || row.Period.Status != ActiveStatus
                || row.Application.AppliedStartDate != row.Period.StartDate
                || row.Application.AppliedEndDate != row.Period.EndDate)
            || coverageRows.Any(row =>
                row.Item.ClientId != clientId
                || row.Closure.ClientId != clientId
                || row.Closure.Status != ActiveStatus))
        {
            return IssuedMembershipSaleDependenciesResult.Inconsistent();
        }

        var activeCoverageConsumptionIds = coverageRows
            .Where(row => row.Item.CoveringMembershipId == membershipId)
            .Select(row => row.Item.NewConsumptionId)
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .ToHashSet();
        if (consumptionRows
            .Where(row => row.Consumption.ConsumptionType == "negative_coverage")
            .Any(row => !activeCoverageConsumptionIds.Contains(row.Consumption.Id)))
        {
            return IssuedMembershipSaleDependenciesResult.Inconsistent();
        }

        var dependencies = new List<IssuedMembershipSaleDependency>();
        dependencies.AddRange(consumptionRows
            .Where(row => row.Consumption.ConsumptionType == "counted")
            .Select(row => new IssuedMembershipSaleDependency(
                "visit",
                row.Visit.Id,
                BusinessTimeZone.GetBusinessDate(row.Visit.OccurredAt),
                $"counted:{row.Consumption.Id:D}")));
        dependencies.AddRange(freezeRows.Select(freeze =>
            new IssuedMembershipSaleDependency(
                "freeze",
                freeze.Id,
                freeze.StartDate,
                $"{freeze.StartDate:yyyy-MM-dd}/{freeze.EndDate:yyyy-MM-dd}")));
        dependencies.AddRange(nonWorkingRows.Select(row =>
            new IssuedMembershipSaleDependency(
                "non_working_day_application",
                row.Application.Id,
                row.Application.AppliedStartDate,
                $"{row.Application.AppliedStartDate:yyyy-MM-dd}/{row.Application.AppliedEndDate:yyyy-MM-dd}")));
        dependencies.AddRange(coverageRows.Select(row =>
            new IssuedMembershipSaleDependency(
                "negative_coverage",
                row.Item.Id,
                BusinessTimeZone.GetBusinessDate(row.Closure.OccurredAt),
                row.Item.SourceMembershipId == membershipId
                    ? $"source:{row.Closure.Id:D}"
                    : $"covering:{row.Closure.Id:D}")));

        var ordered = dependencies
            .OrderBy(dependency => dependency.DependencyType, StringComparer.Ordinal)
            .ThenBy(dependency => dependency.DependencyId)
            .ToArray();
        return IssuedMembershipSaleDependenciesResult.Prepared(ordered);
    }

    internal static string CreateDependencyToken(
        IssuedMembershipRecord membership,
        PaymentRecord payment,
        IReadOnlyList<IssuedMembershipSaleDependency> dependencies)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            MembershipId = membership.Id,
            MembershipStatus = membership.Status,
            membership.IssuedAt,
            PaymentId = payment.Id,
            PaymentStatus = payment.Status,
            payment.RecordedAt,
            Dependencies = dependencies.Select(dependency => new
            {
                dependency.DependencyType,
                dependency.DependencyId,
                dependency.RelevantDate,
                dependency.Context,
            }),
        });
        return Convert.ToHexString(SHA256.HashData(payload));
    }
}

internal enum IssuedMembershipSaleSourceStatus
{
    Prepared = 1,
    Missing,
    Ineligible,
    Inconsistent,
}

internal sealed record IssuedMembershipSaleSourceResult(
    IssuedMembershipSaleSourceStatus Status,
    IssuedMembershipRecord? Membership,
    PaymentRecord? Payment)
{
    internal static IssuedMembershipSaleSourceResult Prepared(
        IssuedMembershipRecord membership,
        PaymentRecord payment) => new(
            IssuedMembershipSaleSourceStatus.Prepared,
            membership,
            payment);

    internal static IssuedMembershipSaleSourceResult Missing() => new(
        IssuedMembershipSaleSourceStatus.Missing,
        null,
        null);

    internal static IssuedMembershipSaleSourceResult Ineligible() => new(
        IssuedMembershipSaleSourceStatus.Ineligible,
        null,
        null);

    internal static IssuedMembershipSaleSourceResult Inconsistent() => new(
        IssuedMembershipSaleSourceStatus.Inconsistent,
        null,
        null);
}

internal sealed record IssuedMembershipSaleDependenciesResult(
    bool IsConsistent,
    IReadOnlyList<IssuedMembershipSaleDependency> Dependencies)
{
    internal static IssuedMembershipSaleDependenciesResult Prepared(
        IReadOnlyList<IssuedMembershipSaleDependency> dependencies) =>
        new(true, dependencies);

    internal static IssuedMembershipSaleDependenciesResult Inconsistent() =>
        new(false, []);
}
