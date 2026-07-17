using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Modules.NonWorkingDays;
using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Tests.Modules.NonWorkingDays;

public sealed class CorrectNonWorkingDayCommandContractsTests
{
    private static readonly Guid PeriodId = Guid.Parse(
        "11111111-1111-1111-1111-111111111111");
    private static readonly DateOnly ReplacementStartDate = new(2026, 7, 20);
    private static readonly DateOnly ReplacementEndDate = new(2026, 7, 22);

    [Fact]
    public void RangeReplacementCarriesExactPreviewInputAndOperationalEnvelope()
    {
        var envelope = CreateEnvelope();
        var command = new CorrectNonWorkingDayCommand(
            envelope,
            PeriodId,
            NonWorkingDayCorrectionMode.ReplaceRange,
            ReplacementStartDate,
            ReplacementEndDate,
            "maintenance",
            "Boiler replacement",
            "bodylife-nwd-correction-v1.payload.signature");

        Assert.IsAssignableFrom<IBodyLifeCommand>(command);
        Assert.Same(envelope, command.Envelope);
        Assert.Equal(PeriodId, command.PeriodId);
        Assert.Equal(NonWorkingDayCorrectionMode.ReplaceRange, command.Mode);
        Assert.Equal(ReplacementStartDate, command.ReplacementStartDate);
        Assert.Equal(ReplacementEndDate, command.ReplacementEndDate);
        Assert.Equal("maintenance", command.ReplacementReasonCode);
        Assert.Equal("Boiler replacement", command.ReplacementReasonComment);
        Assert.Equal(
            "bodylife-nwd-correction-v1.payload.signature",
            command.ConfirmationToken);
        Assert.Equal("correct-non-working-day-key", command.Envelope.IdempotencyKey);
        Assert.Equal("Owner corrected the closure schedule", command.Envelope.Reason);
        Assert.Equal("Confirmed against the correction preview", command.Envelope.Comment);
        Assert.Equal(EntryOrigin.Normal, command.Envelope.EntryOrigin);
    }

    [Fact]
    public void ReasonReplacementCarriesNoSyntheticRange()
    {
        var command = new CorrectNonWorkingDayCommand(
            CreateEnvelope(),
            PeriodId,
            NonWorkingDayCorrectionMode.ReplaceReason,
            ReplacementStartDate: null,
            ReplacementEndDate: null,
            ReplacementReasonCode: "weather_closure",
            ReplacementReasonComment: "Corrected explanation",
            ConfirmationToken: "bodylife-nwd-correction-v1.reason.signature");

        Assert.Equal(NonWorkingDayCorrectionMode.ReplaceReason, command.Mode);
        Assert.Null(command.ReplacementStartDate);
        Assert.Null(command.ReplacementEndDate);
        Assert.Equal("weather_closure", command.ReplacementReasonCode);
        Assert.Equal("Corrected explanation", command.ReplacementReasonComment);
        Assert.NotNull(command.ConfirmationToken);
    }

    [Fact]
    public void CancelCarriesNoSyntheticReplacement()
    {
        var command = new CorrectNonWorkingDayCommand(
            CreateEnvelope(),
            PeriodId,
            NonWorkingDayCorrectionMode.Cancel,
            ReplacementStartDate: null,
            ReplacementEndDate: null,
            ReplacementReasonCode: null,
            ReplacementReasonComment: null,
            ConfirmationToken: "bodylife-nwd-correction-v1.cancel.signature");

        Assert.Equal(NonWorkingDayCorrectionMode.Cancel, command.Mode);
        Assert.Null(command.ReplacementStartDate);
        Assert.Null(command.ReplacementEndDate);
        Assert.Null(command.ReplacementReasonCode);
        Assert.Null(command.ReplacementReasonComment);
        Assert.NotNull(command.ConfirmationToken);
    }

    [Fact]
    public void EntityContractsTargetSourcePeriodAndAffectedMembershipRereads()
    {
        var command = CreateCancelCommand();
        var cancellationId = Guid.Parse(
            "22222222-2222-2222-2222-222222222222");
        var membershipId = Guid.Parse(
            "33333333-3333-3333-3333-333333333333");
        var auditEntryId = AuditEntryId.New();
        var result = CommandResult.Success(
            new EntityId(
                CorrectNonWorkingDayCommand.CancellationEntityType,
                cancellationId),
            command.CanonicalRereadTargetId,
            relatedEntityIds:
            [
                command.SourcePeriodEntityId,
                new EntityId(
                    CorrectNonWorkingDayCommand.MembershipEntityType,
                    membershipId),
            ],
            auditEntryId: auditEntryId);

        Assert.Equal("non_working_period", CorrectNonWorkingDayCommand.PeriodEntityType);
        Assert.Equal(
            "non_working_period_cancellation",
            CorrectNonWorkingDayCommand.CancellationEntityType);
        Assert.Equal("membership", CorrectNonWorkingDayCommand.MembershipEntityType);
        Assert.Equal(
            new EntityId("non_working_period", PeriodId),
            command.SourcePeriodEntityId);
        Assert.Equal(command.SourcePeriodEntityId, command.CanonicalRereadTargetId);
        Assert.Equal(
            new EntityId("non_working_period_cancellation", cancellationId),
            result.PrimaryEntityId);
        Assert.Equal(command.CanonicalRereadTargetId, result.RereadTargetId);
        Assert.Contains(command.SourcePeriodEntityId, result.RelatedEntityIds);
        Assert.Contains(
            new EntityId("membership", membershipId),
            result.RelatedEntityIds);
        Assert.Equal(auditEntryId, result.AuditEntryId);
    }

    [Theory]
    [InlineData(CommandErrorCode.PermissionDenied)]
    [InlineData(CommandErrorCode.ValidationFailed)]
    [InlineData(CommandErrorCode.NotFound)]
    [InlineData(CommandErrorCode.StaleState)]
    [InlineData(CommandErrorCode.AlreadyCanceled)]
    [InlineData(CommandErrorCode.ReasonRequired)]
    [InlineData(CommandErrorCode.PreviewExpired)]
    [InlineData(CommandErrorCode.AffectedScopeChanged)]
    [InlineData(CommandErrorCode.RecalculationFailed)]
    [InlineData(CommandErrorCode.ConcurrencyConflict)]
    public void ResultTaxonomyIncludesDocumentedCorrectionErrors(
        CommandErrorCode errorCode)
    {
        var result = CommandResult.Error(
        [
            new CommandError(
                errorCode,
                "NonWorkingDay cannot be corrected.",
                "periodId"),
        ]);

        Assert.Equal(errorCode, Assert.Single(result.Errors).Code);
        Assert.Null(result.PrimaryEntityId);
        Assert.Null(result.RereadTargetId);
        Assert.Null(result.AuditEntryId);
    }

    private static CorrectNonWorkingDayCommand CreateCancelCommand()
    {
        return new CorrectNonWorkingDayCommand(
            CreateEnvelope(),
            PeriodId,
            NonWorkingDayCorrectionMode.Cancel,
            ReplacementStartDate: null,
            ReplacementEndDate: null,
            ReplacementReasonCode: null,
            ReplacementReasonComment: null,
            ConfirmationToken: "bodylife-nwd-correction-v1.cancel.signature");
    }

    private static CommandEnvelope CreateEnvelope()
    {
        return new CommandEnvelope(
            new ActorContext(
                AccountId.New(),
                ActorRole.Owner,
                AccountKind.Owner,
                SessionId.New(),
                "Owner laptop"),
            new RequestCorrelationId("correlation-correct-non-working-day"),
            EntryOrigin.Normal,
            new DateTimeOffset(2026, 7, 17, 14, 0, 0, TimeSpan.Zero),
            "correct-non-working-day-key",
            "Owner corrected the closure schedule",
            "Confirmed against the correction preview");
    }
}
