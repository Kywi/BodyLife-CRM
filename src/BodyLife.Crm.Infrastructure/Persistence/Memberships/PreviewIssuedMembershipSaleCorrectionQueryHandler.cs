using System.Data;
using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.Infrastructure.Persistence.MembershipTypes;
using BodyLife.Crm.Modules.Memberships;
using BodyLife.Crm.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BodyLife.Crm.Infrastructure.Persistence.Memberships;

public sealed class PreviewIssuedMembershipSaleCorrectionQueryHandler(
    BodyLifeDbContext dbContext,
    TimeProvider timeProvider)
    : IBodyLifeQueryHandler<
        PreviewIssuedMembershipSaleCorrectionQuery,
        PreviewIssuedMembershipSaleCorrectionResult>
{
    public async Task<PreviewIssuedMembershipSaleCorrectionResult> ExecuteAsync(
        PreviewIssuedMembershipSaleCorrectionQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.RepeatableRead,
            cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(
            "set transaction read only",
            cancellationToken);

        async Task<PreviewIssuedMembershipSaleCorrectionResult> CompleteAsync(
            PreviewIssuedMembershipSaleCorrectionResult result)
        {
            await transaction.CommitAsync(cancellationToken);
            return result;
        }

        if (!await MembershipQuerySupport.IsActorAuthorizedAsync(
                dbContext,
                query.Actor,
                timeProvider.GetUtcNow(),
                cancellationToken))
        {
            return await CompleteAsync(Failure(
                PreviewIssuedMembershipSaleCorrectionStatus.PermissionDenied,
                "permission_denied",
                "An active Owner, named Admin or shared Reception/Admin session is required."));
        }

        if (query.OriginalMembershipId == Guid.Empty)
        {
            return await CompleteAsync(Failure(
                PreviewIssuedMembershipSaleCorrectionStatus.ValidationFailed,
                "validation_failed",
                "Original Membership id is required.",
                "originalMembershipId"));
        }

        var hasReplacementType = query.ReplacementMembershipTypeId.HasValue;
        var hasReplacementStart = query.ReplacementStartDate.HasValue;
        if (hasReplacementType != hasReplacementStart
            || query.ReplacementMembershipTypeId is { } selectedTypeId
                && selectedTypeId == Guid.Empty
            || query.ReplacementStartDate is { } selectedStartDate
                && selectedStartDate == default)
        {
            return await CompleteAsync(Failure(
                PreviewIssuedMembershipSaleCorrectionStatus.ValidationFailed,
                "validation_failed",
                "Replacement Membership type and start date must be selected together.",
                "replacement"));
        }

        var source = await IssuedMembershipSaleCorrectionSupport.ReadActiveSaleAsync(
            dbContext,
            query.OriginalMembershipId,
            cancellationToken);
        if (source.Status == IssuedMembershipSaleSourceStatus.Missing)
        {
            return await CompleteAsync(Failure(
                PreviewIssuedMembershipSaleCorrectionStatus.NotFound,
                "not_found",
                "Issued Membership sale was not found.",
                "originalMembershipId"));
        }

        if (source.Status == IssuedMembershipSaleSourceStatus.Ineligible)
        {
            return await CompleteAsync(Failure(
                PreviewIssuedMembershipSaleCorrectionStatus.ValidationFailed,
                "membership_not_eligible",
                "Only an active, uncorrected Membership sale can be changed.",
                "originalMembershipId"));
        }

        if (source.Status != IssuedMembershipSaleSourceStatus.Prepared)
        {
            return await CompleteAsync(Failure(
                PreviewIssuedMembershipSaleCorrectionStatus.CanonicalStateInvalid,
                "canonical_state_invalid",
                "The Membership sale and its exact Payment are inconsistent."));
        }

        var membership = source.Membership!;
        var payment = source.Payment!;
        var dependencies = await IssuedMembershipSaleCorrectionSupport
            .ReadDependenciesAsync(
                dbContext,
                membership.Id,
                membership.ClientId,
                cancellationToken);
        if (!dependencies.IsConsistent)
        {
            return await CompleteAsync(Failure(
                PreviewIssuedMembershipSaleCorrectionStatus.CanonicalStateInvalid,
                "canonical_state_invalid",
                "The Membership dependency set is inconsistent."));
        }

        IssuedMembershipSaleReplacementTerms? replacement = null;
        if (query.ReplacementMembershipTypeId is { } replacementTypeId)
        {
            var membershipType = await dbContext.Set<MembershipTypeRecord>()
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    row => row.Id == replacementTypeId,
                    cancellationToken);
            if (membershipType is null)
            {
                return await CompleteAsync(Failure(
                    PreviewIssuedMembershipSaleCorrectionStatus.NotFound,
                    "not_found",
                    "Replacement Membership type was not found.",
                    "replacementMembershipTypeId"));
            }

            if (!IsEligibleOrdinaryType(membershipType))
            {
                return await CompleteAsync(Failure(
                    PreviewIssuedMembershipSaleCorrectionStatus.ValidationFailed,
                    membershipType.IsActive
                        ? "membership_not_eligible"
                        : "membership_type_inactive",
                    membershipType.IsActive
                        ? "Only an ordinary Membership type can replace a sale."
                        : "Inactive Membership type cannot replace a sale.",
                    "replacementMembershipTypeId"));
            }

            DateOnly baseEndDate;
            try
            {
                baseEndDate = MembershipDateRules.CalculateBaseEndDate(
                    query.ReplacementStartDate!.Value,
                    membershipType.DurationDays);
            }
            catch (ArgumentOutOfRangeException)
            {
                return await CompleteAsync(Failure(
                    PreviewIssuedMembershipSaleCorrectionStatus.ValidationFailed,
                    "validation_failed",
                    "Replacement start date and duration exceed the supported calendar range.",
                    "replacementStartDate"));
            }

            replacement = new IssuedMembershipSaleReplacementTerms(
                membershipType.Id,
                membershipType.UpdatedAt,
                membershipType.Name,
                membershipType.DurationDays,
                membershipType.VisitsLimit,
                new Money(
                    membershipType.PriceAmount,
                    membershipType.PriceCurrency),
                query.ReplacementStartDate.Value,
                baseEndDate);
        }

        var preview = new IssuedMembershipSaleCorrectionPreview(
            new IssuedMembershipSaleDetails(
                membership.Id,
                payment.Id,
                membership.TypeNameSnapshot,
                membership.DurationDaysSnapshot,
                membership.VisitsLimitSnapshot,
                new Money(
                    membership.PriceAmountSnapshot,
                    membership.PriceCurrencySnapshot),
                membership.StartDate,
                membership.BaseEndDate,
                membership.IssuedAt),
            dependencies.Dependencies,
            IssuedMembershipSaleCorrectionSupport.CreateDependencyToken(
                membership,
                payment,
                dependencies.Dependencies),
            replacement);
        return await CompleteAsync(
            PreviewIssuedMembershipSaleCorrectionResult.Succeeded(preview));
    }

    private static bool IsEligibleOrdinaryType(MembershipTypeRecord membershipType)
    {
        return membershipType.IsActive
            && membershipType.Kind == "ordinary"
            && !string.IsNullOrWhiteSpace(membershipType.Name)
            && membershipType.DurationDays > 0
            && membershipType.VisitsLimit >= 0
            && membershipType.PriceAmount > 0
            && !string.IsNullOrWhiteSpace(membershipType.PriceCurrency)
            && membershipType.PriceCurrency
                == membershipType.PriceCurrency.Trim().ToUpperInvariant();
    }

    private static PreviewIssuedMembershipSaleCorrectionResult Failure(
        PreviewIssuedMembershipSaleCorrectionStatus status,
        string code,
        string message,
        string? field = null)
    {
        return PreviewIssuedMembershipSaleCorrectionResult.Failure(
            status,
            code,
            message,
            field);
    }
}
