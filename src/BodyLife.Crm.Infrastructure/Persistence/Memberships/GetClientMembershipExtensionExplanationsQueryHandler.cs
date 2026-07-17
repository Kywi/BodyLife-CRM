using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.Infrastructure.Persistence.ClientsSearch;
using BodyLife.Crm.Infrastructure.Persistence.Freezes;
using BodyLife.Crm.Infrastructure.Persistence.NonWorkingDays;
using BodyLife.Crm.Modules.Memberships;
using BodyLife.Crm.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BodyLife.Crm.Infrastructure.Persistence.Memberships;

public sealed class GetClientMembershipExtensionExplanationsQueryHandler(
    BodyLifeDbContext dbContext,
    TimeProvider timeProvider)
    : IBodyLifeQueryHandler<
        GetClientMembershipExtensionExplanationsQuery,
        GetClientMembershipExtensionExplanationsResult>
{
    public async Task<GetClientMembershipExtensionExplanationsResult> ExecuteAsync(
        GetClientMembershipExtensionExplanationsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!await MembershipQuerySupport.IsActorAuthorizedAsync(
                dbContext,
                query.Actor,
                timeProvider.GetUtcNow(),
                cancellationToken))
        {
            return GetClientMembershipExtensionExplanationsResult.Denied();
        }

        if (query.ClientId == Guid.Empty)
        {
            return GetClientMembershipExtensionExplanationsResult.Invalid(
                "Client id is required.",
                "clientId");
        }

        var clientExists = await dbContext.Set<ClientRecord>()
            .AsNoTracking()
            .AnyAsync(client => client.Id == query.ClientId, cancellationToken);
        if (!clientExists)
        {
            return GetClientMembershipExtensionExplanationsResult.MissingClient();
        }

        var freezeRows = await dbContext.Set<FreezeRecord>()
            .AsNoTracking()
            .Where(freeze => freeze.ClientId == query.ClientId)
            .Select(freeze => new FreezeExplanationRow(
                freeze.Id,
                freeze.MembershipId,
                freeze.StartDate,
                freeze.EndDate,
                freeze.Reason,
                freeze.Status))
            .ToArrayAsync(cancellationToken);
        var nonWorkingRows = await (
            from application in dbContext.Set<NonWorkingPeriodApplicationRecord>()
                .AsNoTracking()
            join period in dbContext.Set<NonWorkingPeriodRecord>().AsNoTracking()
                on application.NonWorkingPeriodId equals period.Id
            where application.ClientId == query.ClientId
            select new NonWorkingExplanationRow(
                application.Id,
                period.Id,
                application.MembershipId,
                application.AppliedStartDate,
                application.AppliedEndDate,
                period.ReasonCode,
                period.ReasonComment,
                application.Status,
                period.Status))
            .ToArrayAsync(cancellationToken);

        var explanations = new List<MembershipExtensionExplanation>(
            freezeRows.Length + nonWorkingRows.Length);

        foreach (var row in freezeRows)
        {
            if (!TryMapFreezeStatus(row.Status, out var status)
                || !TryBuildReasonLabel(row.Reason, comment: null, out var reasonLabel))
            {
                return GetClientMembershipExtensionExplanationsResult.InconsistentSource();
            }

            if (!query.IncludeInactiveSources
                && status != MembershipExtensionSourceStatus.Active)
            {
                continue;
            }

            explanations.Add(new MembershipExtensionExplanation(
                row.MembershipId,
                MembershipExtensionSourceKind.Freeze,
                row.FreezeId,
                nonWorkingPeriodId: null,
                new DateRange(row.StartDate, row.EndDate),
                status,
                reasonLabel));
        }

        foreach (var row in nonWorkingRows)
        {
            if (!TryMapNonWorkingStatus(
                    row.ApplicationStatus,
                    row.PeriodStatus,
                    out var status)
                || !TryBuildReasonLabel(
                    row.ReasonCode,
                    row.ReasonComment,
                    out var reasonLabel))
            {
                return GetClientMembershipExtensionExplanationsResult.InconsistentSource();
            }

            if (!query.IncludeInactiveSources
                && status != MembershipExtensionSourceStatus.Active)
            {
                continue;
            }

            explanations.Add(new MembershipExtensionExplanation(
                row.MembershipId,
                MembershipExtensionSourceKind.NonWorkingDay,
                row.ApplicationId,
                row.PeriodId,
                new DateRange(row.StartDate, row.EndDate),
                status,
                reasonLabel));
        }

        var orderedExplanations = explanations
            .OrderBy(explanation => explanation.MembershipId)
            .ThenBy(explanation => explanation.IsActive ? 0 : 1)
            .ThenByDescending(explanation => explanation.Range.StartDate)
            .ThenByDescending(explanation => explanation.Range.EndDate)
            .ThenBy(explanation => explanation.SourceKind)
            .ThenBy(explanation => explanation.SourceId)
            .ToArray();

        return GetClientMembershipExtensionExplanationsResult.Succeeded(
            new ClientMembershipExtensionExplanations(
                query.ClientId,
                orderedExplanations));
    }

    private static bool TryMapFreezeStatus(
        string status,
        out MembershipExtensionSourceStatus mappedStatus)
    {
        mappedStatus = status switch
        {
            "active" => MembershipExtensionSourceStatus.Active,
            "canceled" => MembershipExtensionSourceStatus.Canceled,
            _ => default,
        };

        return mappedStatus != default;
    }

    private static bool TryMapNonWorkingStatus(
        string applicationStatus,
        string periodStatus,
        out MembershipExtensionSourceStatus mappedStatus)
    {
        if (!TryMapNonWorkingComponentStatus(
                applicationStatus,
                out var applicationMappedStatus)
            || !TryMapNonWorkingComponentStatus(
                periodStatus,
                out var periodMappedStatus))
        {
            mappedStatus = default;
            return false;
        }

        mappedStatus = applicationMappedStatus == MembershipExtensionSourceStatus.Active
            && periodMappedStatus == MembershipExtensionSourceStatus.Active
                ? MembershipExtensionSourceStatus.Active
                : applicationMappedStatus == MembershipExtensionSourceStatus.Corrected
                    || periodMappedStatus == MembershipExtensionSourceStatus.Corrected
                    ? MembershipExtensionSourceStatus.Corrected
                    : MembershipExtensionSourceStatus.Canceled;
        return true;
    }

    private static bool TryMapNonWorkingComponentStatus(
        string status,
        out MembershipExtensionSourceStatus mappedStatus)
    {
        mappedStatus = status switch
        {
            "active" => MembershipExtensionSourceStatus.Active,
            "canceled" => MembershipExtensionSourceStatus.Canceled,
            "corrected" => MembershipExtensionSourceStatus.Corrected,
            _ => default,
        };

        return mappedStatus != default;
    }

    private static bool TryBuildReasonLabel(
        string reason,
        string? comment,
        out string reasonLabel)
    {
        var normalizedReason = reason.Trim();
        var normalizedComment = comment?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedReason))
        {
            reasonLabel = string.Empty;
            return false;
        }

        reasonLabel = string.IsNullOrWhiteSpace(normalizedComment)
            ? normalizedReason
            : $"{normalizedReason} - {normalizedComment}";
        if (reasonLabel.Length > MembershipExtensionSourceRange.MaxSourceLabelLength)
        {
            reasonLabel = reasonLabel[..MembershipExtensionSourceRange.MaxSourceLabelLength];
        }

        return true;
    }

    private sealed record FreezeExplanationRow(
        Guid FreezeId,
        Guid MembershipId,
        DateOnly StartDate,
        DateOnly EndDate,
        string Reason,
        string Status);

    private sealed record NonWorkingExplanationRow(
        Guid ApplicationId,
        Guid PeriodId,
        Guid MembershipId,
        DateOnly StartDate,
        DateOnly EndDate,
        string ReasonCode,
        string? ReasonComment,
        string ApplicationStatus,
        string PeriodStatus);
}
