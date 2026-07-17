using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.Modules.NonWorkingDays;
using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Tests.Modules.NonWorkingDays;

public sealed class PreviewCorrectNonWorkingDayContractsTests
{
    [Fact]
    public void QueryCarriesModeSpecificReplacementInput()
    {
        var actor = new ActorContext(
            AccountId.New(),
            ActorRole.Owner,
            AccountKind.Owner,
            SessionId.New(),
            "Owner laptop");
        var periodId = Guid.NewGuid();
        var query = new PreviewCorrectNonWorkingDayQuery(
            actor,
            periodId,
            NonWorkingDayCorrectionMode.ReplaceRange,
            new DateOnly(2026, 7, 20),
            new DateOnly(2026, 7, 21),
            "maintenance",
            "Boiler replacement");

        Assert.IsAssignableFrom<IBodyLifeQuery<PreviewCorrectNonWorkingDayResult>>(query);
        Assert.Same(actor, query.Actor);
        Assert.Equal(periodId, query.PeriodId);
        Assert.Equal(NonWorkingDayCorrectionMode.ReplaceRange, query.Mode);
        Assert.Equal(new DateOnly(2026, 7, 20), query.ReplacementStartDate);
        Assert.Equal(new DateOnly(2026, 7, 21), query.ReplacementEndDate);
        Assert.Equal("maintenance", query.ReplacementReasonCode);
        Assert.Equal("Boiler replacement", query.ReplacementReasonComment);
    }

    [Fact]
    public void ResultFactoriesExposeStableQueryErrorTaxonomy()
    {
        var cases = new[]
        {
            (
                PreviewCorrectNonWorkingDayResult.Denied(),
                PreviewCorrectNonWorkingDayStatus.PermissionDenied,
                "permission_denied"),
            (
                PreviewCorrectNonWorkingDayResult.Invalid(" Invalid input ", "mode"),
                PreviewCorrectNonWorkingDayStatus.ValidationFailed,
                "validation_failed"),
            (
                PreviewCorrectNonWorkingDayResult.Missing(),
                PreviewCorrectNonWorkingDayStatus.NotFound,
                "not_found"),
            (
                PreviewCorrectNonWorkingDayResult.AlreadyCanceled(),
                PreviewCorrectNonWorkingDayStatus.AlreadyCanceled,
                "already_canceled"),
            (
                PreviewCorrectNonWorkingDayResult.Stale(),
                PreviewCorrectNonWorkingDayStatus.StaleState,
                "stale_state"),
            (
                PreviewCorrectNonWorkingDayResult.InconsistentSource(),
                PreviewCorrectNonWorkingDayStatus.SourceInconsistent,
                "source_inconsistent"),
            (
                PreviewCorrectNonWorkingDayResult.RecalculationFailed(),
                PreviewCorrectNonWorkingDayStatus.RecalculationFailed,
                "recalculation_failed"),
        };

        foreach (var (result, status, errorCode) in cases)
        {
            Assert.Equal(status, result.Status);
            Assert.Equal(errorCode, result.ErrorCode);
            Assert.NotNull(result.ErrorMessage);
            Assert.Null(result.Preview);
        }

        Assert.Throws<ArgumentException>(() =>
            PreviewCorrectNonWorkingDayResult.Invalid("  ", field: null));
    }
}
