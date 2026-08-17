using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.Infrastructure.Persistence.ClientsSearch;
using BodyLife.Crm.Infrastructure.Persistence.MembershipTypes;
using BodyLife.Crm.Modules.Memberships;
using BodyLife.Crm.Modules.MembershipTypes;
using BodyLife.Crm.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BodyLife.Crm.Infrastructure.Persistence.Memberships;

public sealed class PreviewIssueMembershipQueryHandler(
    BodyLifeDbContext dbContext,
    MembershipNegativeVisitSelector negativeVisitSelector,
    IMembershipIssuePreviewTokenService previewTokenService,
    TimeProvider timeProvider)
    : IBodyLifeQueryHandler<PreviewIssueMembershipQuery, PreviewIssueMembershipResult>
{
    public async Task<PreviewIssueMembershipResult> ExecuteAsync(
        PreviewIssueMembershipQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var now = timeProvider.GetUtcNow();
        if (!await MembershipQuerySupport.IsActorAuthorizedAsync(
                dbContext,
                query.Actor,
                now,
                cancellationToken))
        {
            return PreviewIssueMembershipResult.Denied();
        }

        if (query.ClientId == Guid.Empty)
        {
            return PreviewIssueMembershipResult.Invalid(
                "Client id is required.",
                "clientId");
        }

        if (query.MembershipTypeId == Guid.Empty)
        {
            return PreviewIssueMembershipResult.Invalid(
                "Membership type id is required.",
                "membershipTypeId");
        }

        if (query.ProposedStartDate == default)
        {
            return PreviewIssueMembershipResult.Invalid(
                "Proposed start date is required.",
                "proposedStartDate");
        }

        var clientExists = await dbContext.Set<ClientRecord>()
            .AsNoTracking()
            .AnyAsync(client => client.Id == query.ClientId, cancellationToken);
        if (!clientExists)
        {
            return PreviewIssueMembershipResult.MissingClient();
        }

        var membershipType = await dbContext.Set<MembershipTypeRecord>()
            .AsNoTracking()
            .Where(record => record.Id == query.MembershipTypeId)
            .Select(record => new MembershipTypeRow(
                record.Id,
                record.Name,
                record.DurationDays,
                record.VisitsLimit,
                record.PriceAmount,
                record.PriceCurrency,
                record.Kind,
                record.IsActive,
                record.Comment,
                record.CreatedAt,
                record.UpdatedAt,
                record.DeactivatedAt))
            .SingleOrDefaultAsync(cancellationToken);
        if (membershipType is null)
        {
            return PreviewIssueMembershipResult.MissingMembershipType();
        }

        if (!membershipType.IsActive)
        {
            return PreviewIssueMembershipResult.InactiveMembershipType();
        }

        if (!string.Equals(membershipType.Kind, "ordinary", StringComparison.Ordinal))
        {
            return PreviewIssueMembershipResult.Invalid(
                "Only ordinary membership types can be issued as a membership sale.",
                "membershipTypeId");
        }

        var selectionResult = await negativeVisitSelector.SelectAsync(
            query.ClientId,
            cancellationToken);
        if (selectionResult.Status
            != MembershipNegativeVisitSelectionStatus.Succeeded)
        {
            return PreviewIssueMembershipResult.RecalculationFailed();
        }

        var selection = selectionResult.Selection!;
        var existingNegativeState = selection.TotalNegativeBalance > 0
            ? new MembershipIssueNegativeContext(
                selection.TotalNegativeBalance,
                selection.FirstNegativeVisitDate,
                selection.OpenConcreteVisits)
            : null;
        MembershipIssuePreview preview;

        try
        {
            var catalogItem = new MembershipTypeCatalogItem(
                membershipType.MembershipTypeId,
                membershipType.Name,
                membershipType.DurationDays,
                membershipType.VisitsLimit,
                new Money(membershipType.PriceAmount, membershipType.PriceCurrency),
                membershipType.IsActive,
                membershipType.Comment,
                membershipType.CreatedAt,
                membershipType.UpdatedAt,
                membershipType.DeactivatedAt,
                MembershipTypeKind.Ordinary);
            preview = MembershipIssuePreviewPolicy.Create(
                query.ClientId,
                catalogItem,
                query.ProposedStartDate,
                existingNegativeState,
                BusinessTimeZone.GetBusinessDate(now));
        }
        catch (ArgumentOutOfRangeException exception)
            when (exception.ParamName == "durationDays")
        {
            return PreviewIssueMembershipResult.Invalid(
                "Proposed start date and membership duration exceed the supported calendar range.",
                "proposedStartDate");
        }
        catch (ArgumentException)
        {
            return PreviewIssueMembershipResult.Invalid(
                "Membership type data cannot produce a valid issue preview.",
                "membershipTypeId");
        }
        catch (InvalidOperationException)
        {
            return PreviewIssueMembershipResult.InactiveMembershipType();
        }

        var token = previewTokenService.Issue(new MembershipIssuePreviewTokenMaterial(
            query.ClientId,
            membershipType.MembershipTypeId,
            membershipType.UpdatedAt,
            query.ProposedStartDate,
            selection.TotalNegativeBalance,
            existingNegativeState?.UnknownNegativeBalance ?? 0,
            selection.OpenConcreteVisits,
            preview.AutomaticCoveredNegativeVisitCount));
        return PreviewIssueMembershipResult.Succeeded(
            preview,
            MembershipQuerySupport.BuildIssueActionPermissions(),
            token);
    }

    private sealed record MembershipTypeRow(
        Guid MembershipTypeId,
        string Name,
        int DurationDays,
        int VisitsLimit,
        decimal PriceAmount,
        string PriceCurrency,
        string Kind,
        bool IsActive,
        string? Comment,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt,
        DateTimeOffset? DeactivatedAt);

}
