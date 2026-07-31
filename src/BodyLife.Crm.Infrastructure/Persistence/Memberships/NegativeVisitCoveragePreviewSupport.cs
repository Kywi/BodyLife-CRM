using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Infrastructure.Persistence.MembershipTypes;
using BodyLife.Crm.Modules.Memberships;
using BodyLife.Crm.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BodyLife.Crm.Infrastructure.Persistence.Memberships;

internal static class NegativeVisitCoveragePreviewSupport
{
    private const int MaximumLines = 50;

    internal static PreviewInputError? NormalizeOneOffLines(
        IReadOnlyList<NegativeVisitClosureLineSelection>? lines,
        string fieldPrefix,
        bool required,
        out IReadOnlyList<NormalizedOneOffNegativeClosureLine> normalized,
        out int visitsCount)
    {
        normalized = [];
        visitsCount = 0;
        if (lines is null || lines.Count == 0)
        {
            return required
                ? new PreviewInputError(
                    CommandErrorCode.ValidationFailed,
                    $"One to {MaximumLines} one-off lines are required.",
                    fieldPrefix)
                : null;
        }

        if (lines.Count > MaximumLines)
        {
            return new PreviewInputError(
                CommandErrorCode.ValidationFailed,
                $"At most {MaximumLines} one-off lines are supported.",
                fieldPrefix);
        }

        var output = new List<NormalizedOneOffNegativeClosureLine>(lines.Count);
        var typeIds = new HashSet<Guid>();
        long quantity = 0;
        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            if (line is null)
            {
                return new PreviewInputError(
                    CommandErrorCode.ValidationFailed,
                    "One-off lines cannot contain a missing item.",
                    $"{fieldPrefix}[{index}]");
            }

            if (line.MembershipTypeId == Guid.Empty
                || !typeIds.Add(line.MembershipTypeId))
            {
                return new PreviewInputError(
                    CommandErrorCode.ValidationFailed,
                    "Every one-off type must be non-empty and unique.",
                    $"{fieldPrefix}[{index}].membershipTypeId");
            }

            if (line.ExpectedMembershipTypeUpdatedAt == default)
            {
                return new PreviewInputError(
                    CommandErrorCode.ValidationFailed,
                    "Expected one-off membership type version is required.",
                    $"{fieldPrefix}[{index}].expectedMembershipTypeUpdatedAt");
            }

            if (line.Quantity <= 0)
            {
                return new PreviewInputError(
                    CommandErrorCode.ValidationFailed,
                    "One-off quantity must be positive.",
                    $"{fieldPrefix}[{index}].quantity");
            }

            quantity += line.Quantity;
            if (quantity > int.MaxValue)
            {
                return new PreviewInputError(
                    CommandErrorCode.ValidationFailed,
                    "One-off quantity exceeds the supported range.",
                    fieldPrefix);
            }

            output.Add(new NormalizedOneOffNegativeClosureLine(
                line.MembershipTypeId,
                line.ExpectedMembershipTypeUpdatedAt.ToUniversalTime(),
                line.Quantity,
                index + 1));
        }

        normalized = output.AsReadOnly();
        visitsCount = (int)quantity;
        return null;
    }

    internal static async Task<NegativeVisitCoverageStaleSelectors?> LoadSelectorsAsync(
        BodyLifeDbContext dbContext,
        Guid? currentOldestOpenNegativeVisitId,
        CancellationToken cancellationToken)
    {
        var records = await dbContext.Set<MembershipTypeRecord>()
            .AsNoTracking()
            .Where(record => record.IsActive && record.Kind == "one_off")
            .OrderBy(record => record.Name)
            .ThenBy(record => record.Id)
            .ToArrayAsync(cancellationToken);
        var selectors = new List<OneOffNegativeClosureSelectorReadModel>(records.Length);
        foreach (var record in records)
        {
            if (string.IsNullOrWhiteSpace(record.Name)
                || record.DurationDays <= 0
                || record.VisitsLimit != 1
                || record.PriceAmount <= 0
                || !TryMoney(record.PriceAmount, record.PriceCurrency, out var price))
            {
                return null;
            }

            selectors.Add(new OneOffNegativeClosureSelectorReadModel(
                record.Id,
                record.Name,
                record.DurationDays,
                record.VisitsLimit,
                price,
                record.UpdatedAt));
        }

        return new NegativeVisitCoverageStaleSelectors(
            currentOldestOpenNegativeVisitId,
            selectors.AsReadOnly());
    }

    internal static IReadOnlyList<OneOffNegativeClosurePreviewLine> CreatePreviewLines(
        PreparedOneOffClosureLines preparation) => preparation.Lines
        .Select(line => new OneOffNegativeClosurePreviewLine(
            line.Record.Id,
            line.Record.Name,
            line.Selection.Quantity,
            new Money(line.Record.PriceAmount, line.Record.PriceCurrency),
            new Money(line.LineTotal, line.Record.PriceCurrency),
            line.Record.UpdatedAt,
            line.Selection.Sequence))
        .ToArray();

    internal static IReadOnlyList<NegativeVisitCoverageCandidateReadModel> OrderCandidates(
        IEnumerable<NegativeVisitCoverageCandidateReadModel> candidates) => candidates
        .OrderBy(candidate => candidate.OccurredAt)
        .ThenBy(candidate => candidate.ConsumptionRecordedAt)
        .ThenBy(candidate => candidate.VisitId)
        .ThenBy(candidate => candidate.SourceMembershipId)
        .ToArray();

    internal static NegativeVisitCoverageCandidateReadModel ToReadModel(
        MembershipNegativeVisitCoverageCandidate candidate) => new(
        candidate.VisitId,
        candidate.SourceMembershipId,
        candidate.OldConsumptionId,
        candidate.OccurredAt,
        candidate.ConsumptionRecordedAt,
        candidate.BusinessDate);

    internal static PreviewInputError FromCommandError(CommandResult result)
    {
        var error = result.Errors.FirstOrDefault()
            ?? throw new InvalidOperationException("One-off preparation failure has no error.");
        return new PreviewInputError(error.Code, error.Message, error.Field);
    }

    internal static IssuedMembershipCoverageSnapshotReadModel CreateMembershipSnapshot(
        IssuedMembershipRecord membership) => new(
        membership.Id,
        membership.MembershipTypeId,
        membership.TypeNameSnapshot,
        membership.DurationDaysSnapshot,
        membership.VisitsLimitSnapshot,
        new Money(
            membership.PriceAmountSnapshot,
            membership.PriceCurrencySnapshot),
        membership.StartDate,
        membership.BaseEndDate,
        membership.IssuedAt,
        membership.Status);

    internal static bool TryMoney(decimal amount, string? currency, out Money money)
    {
        try
        {
            money = new Money(amount, currency!);
            return true;
        }
        catch (ArgumentException)
        {
            money = default;
            return false;
        }
    }
}

internal sealed record PreviewInputError(
    CommandErrorCode Code,
    string Message,
    string? Field);
