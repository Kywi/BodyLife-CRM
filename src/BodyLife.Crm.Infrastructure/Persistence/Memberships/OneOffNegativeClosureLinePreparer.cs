using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Infrastructure.Persistence.MembershipTypes;
using Microsoft.EntityFrameworkCore;

namespace BodyLife.Crm.Infrastructure.Persistence.Memberships;

internal static class OneOffNegativeClosureLinePreparer
{
    internal static async Task<PreparedOneOffClosureLinesResult> PrepareAsync(
        BodyLifeDbContext dbContext,
        IReadOnlyList<NormalizedOneOffNegativeClosureLine> selections,
        string fieldPrefix,
        CancellationToken cancellationToken)
    {
        return await PrepareCoreAsync(
            dbContext,
            selections,
            fieldPrefix,
            lockRows: true,
            cancellationToken);
    }

    internal static async Task<PreparedOneOffClosureLinesResult> PrepareReadOnlyAsync(
        BodyLifeDbContext dbContext,
        IReadOnlyList<NormalizedOneOffNegativeClosureLine> selections,
        string fieldPrefix,
        CancellationToken cancellationToken)
    {
        return await PrepareCoreAsync(
            dbContext,
            selections,
            fieldPrefix,
            lockRows: false,
            cancellationToken);
    }

    private static async Task<PreparedOneOffClosureLinesResult> PrepareCoreAsync(
        BodyLifeDbContext dbContext,
        IReadOnlyList<NormalizedOneOffNegativeClosureLine> selections,
        string fieldPrefix,
        bool lockRows,
        CancellationToken cancellationToken)
    {
        var typeIds = selections
            .Select(line => line.MembershipTypeId)
            .Order()
            .ToArray();
        MembershipTypeRecord[] typeRows;
        if (lockRows)
        {
            typeRows = await dbContext.Set<MembershipTypeRecord>()
                .FromSqlInterpolated(
                    $"""
                    select *
                    from bodylife.membership_types
                    where id = any ({typeIds})
                    order by id
                    for share
                    """)
                .AsNoTracking()
                .ToArrayAsync(cancellationToken);
        }
        else
        {
            typeRows = await dbContext.Set<MembershipTypeRecord>()
                .AsNoTracking()
                .Where(record => typeIds.Contains(record.Id))
                .OrderBy(record => record.Id)
                .ToArrayAsync(cancellationToken);
        }
        if (typeRows.Length != selections.Count)
        {
            return PreparedOneOffClosureLinesResult.Failed(
                    NegativeCoverageCommandSupport.Error(
                        CommandErrorCode.NotFound,
                        "One or more one-off membership types were not found.",
                        fieldPrefix));
        }

        var recordsById = typeRows.ToDictionary(record => record.Id);
        var prepared = new List<PreparedOneOffClosureLine>(selections.Count);
        string? currency = null;
        try
        {
            foreach (var selection in selections)
            {
                var record = recordsById[selection.MembershipTypeId];
                if (!record.IsActive)
                {
                    return PreparedOneOffClosureLinesResult.Failed(
                            NegativeCoverageCommandSupport.Error(
                                CommandErrorCode.MembershipTypeInactive,
                                "Inactive one-off membership type cannot close negative Visits.",
                            $"{fieldPrefix}[{selection.Sequence - 1}].membershipTypeId"));
                }

                if (!string.Equals(record.Kind, "one_off", StringComparison.Ordinal)
                    || record.VisitsLimit != 1
                    || record.PriceAmount <= 0)
                {
                    return PreparedOneOffClosureLinesResult.Failed(
                            NegativeCoverageCommandSupport.Error(
                                CommandErrorCode.MembershipNotEligible,
                                "Selected membership type is not an eligible one-off type.",
                            $"{fieldPrefix}[{selection.Sequence - 1}].membershipTypeId"));
                }

                if (record.UpdatedAt != selection.ExpectedMembershipTypeUpdatedAt)
                {
                    return PreparedOneOffClosureLinesResult.Failed(
                            NegativeCoverageCommandSupport.Error(
                                CommandErrorCode.StaleState,
                                "One-off membership type changed after preview. Refresh canonical state.",
                            $"{fieldPrefix}[{selection.Sequence - 1}].expectedMembershipTypeUpdatedAt"));
                }

                currency ??= record.PriceCurrency;
                if (!string.Equals(currency, record.PriceCurrency, StringComparison.Ordinal))
                {
                    return PreparedOneOffClosureLinesResult.Failed(
                        NegativeCoverageCommandSupport.ValidationError(
                            "All one-off closure lines must use the same currency.",
                            fieldPrefix));
                }

                prepared.Add(new PreparedOneOffClosureLine(
                    selection,
                    record,
                    checked(record.PriceAmount * selection.Quantity)));
            }
        }
        catch (OverflowException)
        {
            return PreparedOneOffClosureLinesResult.Failed(
                NegativeCoverageCommandSupport.ValidationError(
                    "One-off closure total exceeds the supported amount range.",
                    fieldPrefix));
        }

        return PreparedOneOffClosureLinesResult.Completed(
            new PreparedOneOffClosureLines(
                prepared.AsReadOnly(),
                prepared.Sum(line => line.LineTotal),
                currency!,
                prepared.Sum(line => line.Selection.Quantity)));
    }
}

internal sealed record PreparedOneOffClosureLine(
    NormalizedOneOffNegativeClosureLine Selection,
    MembershipTypeRecord Record,
    decimal LineTotal);

internal sealed record PreparedOneOffClosureLines(
    IReadOnlyList<PreparedOneOffClosureLine> Lines,
    decimal TotalAmount,
    string Currency,
    int VisitsCount);

internal sealed record PreparedOneOffClosureLinesResult(
    PreparedOneOffClosureLines? Preparation,
    CommandResult? Error)
{
    internal static PreparedOneOffClosureLinesResult Completed(
        PreparedOneOffClosureLines preparation)
    {
        return new PreparedOneOffClosureLinesResult(preparation, Error: null);
    }

    internal static PreparedOneOffClosureLinesResult Failed(CommandResult error)
    {
        return new PreparedOneOffClosureLinesResult(Preparation: null, error);
    }
}
