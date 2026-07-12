using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.Modules.MembershipTypes;
using BodyLife.Crm.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BodyLife.Crm.Infrastructure.Persistence.MembershipTypes;

public sealed class GetMembershipTypesForIssueQueryHandler(
    BodyLifeDbContext dbContext,
    TimeProvider timeProvider)
    : IBodyLifeQueryHandler<GetMembershipTypesForIssueQuery, GetMembershipTypesForIssueResult>
{
    public async Task<GetMembershipTypesForIssueResult> ExecuteAsync(
        GetMembershipTypesForIssueQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!await MembershipTypeQuerySupport.IsActorAuthorizedAsync(
                dbContext,
                query.Actor,
                timeProvider.GetUtcNow(),
                cancellationToken))
        {
            return GetMembershipTypesForIssueResult.Denied(
                "An active Owner, named Admin or shared Reception/Admin session is required.");
        }

        if (query.IncludeInactive && !MembershipTypeQuerySupport.IsOwnerActor(query.Actor))
        {
            return GetMembershipTypesForIssueResult.Denied(
                "Only an active Owner session can include inactive membership types.");
        }

        var records = dbContext.Set<MembershipTypeRecord>().AsNoTracking();
        IOrderedQueryable<MembershipTypeRecord> orderedRecords;

        if (query.IncludeInactive)
        {
            orderedRecords = records
                .OrderByDescending(record => record.IsActive)
                .ThenBy(record => record.Name)
                .ThenBy(record => record.Id);
        }
        else
        {
            orderedRecords = records
                .Where(record => record.IsActive)
                .OrderBy(record => record.Name)
                .ThenBy(record => record.Id);
        }

        var rows = await orderedRecords
            .Select(record => new MembershipTypeCatalogRow(
                record.Id,
                record.Name,
                record.DurationDays,
                record.VisitsLimit,
                record.PriceAmount,
                record.PriceCurrency,
                record.IsActive,
                record.Comment,
                record.CreatedAt,
                record.UpdatedAt,
                record.DeactivatedAt))
            .ToArrayAsync(cancellationToken);
        var items = rows
            .Select(row => new MembershipTypeCatalogItem(
                row.MembershipTypeId,
                row.Name,
                row.DurationDays,
                row.VisitsLimit,
                new Money(row.PriceAmount, row.PriceCurrency),
                row.IsActive,
                row.Comment,
                row.CreatedAt,
                row.UpdatedAt,
                row.DeactivatedAt))
            .ToArray();

        return GetMembershipTypesForIssueResult.Succeeded(
            items,
            query.IncludeInactive,
            MembershipTypeQuerySupport.BuildActionPermissions(query.Actor));
    }

    private sealed record MembershipTypeCatalogRow(
        Guid MembershipTypeId,
        string Name,
        int DurationDays,
        int VisitsLimit,
        decimal PriceAmount,
        string PriceCurrency,
        bool IsActive,
        string? Comment,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt,
        DateTimeOffset? DeactivatedAt);
}
