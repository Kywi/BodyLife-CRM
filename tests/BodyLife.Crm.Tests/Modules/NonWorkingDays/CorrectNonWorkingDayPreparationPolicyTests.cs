using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Modules.NonWorkingDays;
using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Tests.Modules.NonWorkingDays;

public sealed class CorrectNonWorkingDayPreparationPolicyTests
{
    private static readonly Guid PeriodId = Guid.Parse(
        "11111111-1111-1111-1111-111111111111");
    private static readonly DateOnly ReplacementStartDate = new(2026, 7, 20);
    private static readonly DateOnly ReplacementEndDate = new(2026, 7, 22);

    [Fact]
    public void RangeReplacementReturnsCanonicalPreparation()
    {
        var envelope = CreateEnvelope() with
        {
            Actor = CreateEnvelope().Actor with { DeviceLabel = "  Owner laptop  " },
            RequestCorrelationId = new RequestCorrelationId("  correction-correlation  "),
            OccurredAt = new DateTimeOffset(2026, 7, 17, 16, 0, 0, TimeSpan.FromHours(2)),
            IdempotencyKey = "  correction-key  ",
            Reason = "  Schedule correction  ",
            Comment = "  Owner confirmed exact scope  ",
        };
        var command = CreateRangeCommand(envelope) with
        {
            ReplacementReasonCode = "  cafe\u0301_closure  ",
            ReplacementReasonComment = "  Boiler replacement  ",
        };

        var result = CorrectNonWorkingDayPreparationPolicy.Prepare(command);

        Assert.True(result.IsPrepared);
        Assert.Empty(result.Errors);
        var preparation = Assert.IsType<CorrectNonWorkingDayPreparation>(
            result.Preparation);
        Assert.Equal(PeriodId, preparation.PeriodId);
        Assert.Equal(NonWorkingDayCorrectionMode.ReplaceRange, preparation.Mode);
        Assert.Equal(
            new DateRange(ReplacementStartDate, ReplacementEndDate),
            preparation.ReplacementPeriod);
        Assert.Equal("caf\u00E9_closure", preparation.ReplacementReasonCode);
        Assert.Equal("Boiler replacement", preparation.ReplacementReasonComment);
        Assert.Equal("Owner laptop", preparation.Envelope.Actor.DeviceLabel);
        Assert.Equal(
            "correction-correlation",
            preparation.Envelope.RequestCorrelationId.Value);
        Assert.Equal(
            new DateTimeOffset(2026, 7, 17, 14, 0, 0, TimeSpan.Zero),
            preparation.Envelope.OccurredAt);
        Assert.Equal("correction-key", preparation.Envelope.IdempotencyKey);
        Assert.Equal("Schedule correction", preparation.Envelope.Reason);
        Assert.Equal("Owner confirmed exact scope", preparation.Envelope.Comment);
        Assert.Equal(command.ConfirmationToken, preparation.ConfirmationToken);
        Assert.Equal(command.SourcePeriodEntityId, preparation.SourcePeriodEntityId);
        Assert.Equal(
            command.CanonicalRereadTargetId,
            preparation.CanonicalRereadTargetId);
    }

    [Fact]
    public void ReasonReplacementPreservesNoSyntheticRange()
    {
        var command = CreateReasonCommand() with
        {
            ReplacementReasonCode = "  weather_closure  ",
            ReplacementReasonComment = "  Corrected explanation  ",
        };

        var result = CorrectNonWorkingDayPreparationPolicy.Prepare(command);

        var preparation = Assert.IsType<CorrectNonWorkingDayPreparation>(
            result.Preparation);
        Assert.True(result.IsPrepared);
        Assert.Null(preparation.ReplacementPeriod);
        Assert.Equal("weather_closure", preparation.ReplacementReasonCode);
        Assert.Equal("Corrected explanation", preparation.ReplacementReasonComment);
    }

    [Fact]
    public void CancelCanonicalizesWhitespaceOnlyReplacementTextToNoReplacement()
    {
        var command = CreateCancelCommand() with
        {
            ReplacementReasonCode = "   ",
            ReplacementReasonComment = "   ",
        };

        var result = CorrectNonWorkingDayPreparationPolicy.Prepare(command);

        var preparation = Assert.IsType<CorrectNonWorkingDayPreparation>(
            result.Preparation);
        Assert.True(result.IsPrepared);
        Assert.Null(preparation.ReplacementPeriod);
        Assert.Null(preparation.ReplacementReasonCode);
        Assert.Null(preparation.ReplacementReasonComment);
    }

    [Fact]
    public void PreparationReturnsStableErrorsForPeriodAndMode()
    {
        var missingPeriod = CorrectNonWorkingDayPreparationPolicy.Prepare(
            CreateCancelCommand() with { PeriodId = Guid.Empty });
        var unsupportedMode = CorrectNonWorkingDayPreparationPolicy.Prepare(
            CreateCancelCommand() with { Mode = (NonWorkingDayCorrectionMode)999 });

        AssertRejected(
            missingPeriod,
            CommandErrorCode.ValidationFailed,
            "periodId");
        AssertRejected(
            unsupportedMode,
            CommandErrorCode.ValidationFailed,
            "mode");
    }

    [Fact]
    public void PreparationValidatesCommonActorAndRequestShape()
    {
        var envelope = CreateEnvelope();
        var cases = new (CorrectNonWorkingDayCommand Command, string Field)[]
        {
            (CreateCancelCommand() with { Envelope = null! }, "envelope"),
            (CreateCancelCommand(envelope with { Actor = null! }), "actor"),
            (CreateCancelCommand(envelope with
            {
                Actor = envelope.Actor with { AccountId = default },
            }), "actorAccountId"),
            (CreateCancelCommand(envelope with
            {
                Actor = envelope.Actor with { Role = (ActorRole)999 },
            }), "actorRole"),
            (CreateCancelCommand(envelope with
            {
                Actor = envelope.Actor with { AccountKind = (AccountKind)999 },
            }), "accountKind"),
            (CreateCancelCommand(envelope with
            {
                Actor = envelope.Actor with { SessionId = default },
            }), "sessionId"),
            (CreateCancelCommand(envelope with
            {
                RequestCorrelationId = new RequestCorrelationId("   "),
            }), "requestCorrelationId"),
            (CreateCancelCommand(envelope with
            {
                Actor = envelope.Actor with { DeviceLabel = new string('d', 121) },
            }), "deviceLabel"),
            (CreateCancelCommand(envelope with
            {
                EntryOrigin = (EntryOrigin)999,
            }), "entryOrigin"),
        };

        foreach (var (command, field) in cases)
        {
            AssertRejected(
                CorrectNonWorkingDayPreparationPolicy.Prepare(command),
                CommandErrorCode.ValidationFailed,
                field);
        }
    }

    [Fact]
    public void PreparationValidatesOperationalEnvelopeFields()
    {
        var envelope = CreateEnvelope();
        var cases = new (
            CorrectNonWorkingDayCommand Command,
            CommandErrorCode Code,
            string Field)[]
        {
            (CreateCancelCommand(envelope with { IdempotencyKey = "   " }),
                CommandErrorCode.ValidationFailed, "idempotencyKey"),
            (CreateCancelCommand(envelope with
            {
                IdempotencyKey = new string('k', 201),
            }), CommandErrorCode.ValidationFailed, "idempotencyKey"),
            (CreateCancelCommand(envelope with { OccurredAt = null }),
                CommandErrorCode.ValidationFailed, "occurredAt"),
            (CreateCancelCommand(envelope with { Reason = "   " }),
                CommandErrorCode.ReasonRequired, "reason"),
            (CreateCancelCommand(envelope with { Reason = new string('r', 1001) }),
                CommandErrorCode.ValidationFailed, "reason"),
            (CreateCancelCommand(envelope with { Comment = "   " }),
                CommandErrorCode.ReasonRequired, "comment"),
            (CreateCancelCommand(envelope with { Comment = new string('c', 1001) }),
                CommandErrorCode.ValidationFailed, "comment"),
        };

        foreach (var (command, code, field) in cases)
        {
            AssertRejected(
                CorrectNonWorkingDayPreparationPolicy.Prepare(command),
                code,
                field);
        }
    }

    [Fact]
    public void PreparationRequiresCanonicalConfirmationToken()
    {
        var commands = new[]
        {
            CreateCancelCommand() with { ConfirmationToken = null },
            CreateCancelCommand() with { ConfirmationToken = "   " },
            CreateCancelCommand() with { ConfirmationToken = " token " },
            CreateCancelCommand() with
            {
                ConfirmationToken = new string(
                    't',
                    NonWorkingDayCorrectionConfirmation.MaxTokenLength + 1),
            },
        };

        foreach (var command in commands)
        {
            AssertRejected(
                CorrectNonWorkingDayPreparationPolicy.Prepare(command),
                CommandErrorCode.ValidationFailed,
                "confirmationToken");
        }
    }

    [Fact]
    public void RangeReplacementRequiresValidInclusiveDates()
    {
        var commands = new[]
        {
            (CreateRangeCommand() with { ReplacementStartDate = null },
                "replacementStartDate"),
            (CreateRangeCommand() with { ReplacementStartDate = default(DateOnly) },
                "replacementStartDate"),
            (CreateRangeCommand() with { ReplacementEndDate = null },
                "replacementEndDate"),
            (CreateRangeCommand() with { ReplacementEndDate = default(DateOnly) },
                "replacementEndDate"),
            (CreateRangeCommand() with
            {
                ReplacementEndDate = ReplacementStartDate.AddDays(-1),
            }, "replacementEndDate"),
        };

        foreach (var (command, field) in commands)
        {
            AssertRejected(
                CorrectNonWorkingDayPreparationPolicy.Prepare(command),
                CommandErrorCode.ValidationFailed,
                field);
        }
    }

    [Fact]
    public void RangeReplacementValidatesReplacementReasonFields()
    {
        var commands = new[]
        {
            (CreateRangeCommand() with { ReplacementReasonCode = "   " },
                "replacementReasonCode"),
            (CreateRangeCommand() with
            {
                ReplacementReasonCode = new string(
                    'r',
                    NonWorkingDayPreviewInput.ReasonCodeMaxLength + 1),
            }, "replacementReasonCode"),
            (CreateRangeCommand() with
            {
                ReplacementReasonComment = new string(
                    'c',
                    NonWorkingDayPreviewInput.ReasonCommentMaxLength + 1),
            }, "replacementReasonComment"),
        };

        foreach (var (command, field) in commands)
        {
            AssertRejected(
                CorrectNonWorkingDayPreparationPolicy.Prepare(command),
                CommandErrorCode.ValidationFailed,
                field);
        }
    }

    [Fact]
    public void ReasonReplacementRejectsDatesAndRequiresReplacementReason()
    {
        var commands = new[]
        {
            (CreateReasonCommand() with
            {
                ReplacementStartDate = ReplacementStartDate,
            }, "replacementStartDate"),
            (CreateReasonCommand() with
            {
                ReplacementEndDate = ReplacementEndDate,
            }, "replacementEndDate"),
            (CreateReasonCommand() with { ReplacementReasonCode = "   " },
                "replacementReasonCode"),
        };

        foreach (var (command, field) in commands)
        {
            AssertRejected(
                CorrectNonWorkingDayPreparationPolicy.Prepare(command),
                CommandErrorCode.ValidationFailed,
                field);
        }
    }

    [Fact]
    public void CancelRejectsEveryReplacementField()
    {
        var commands = new[]
        {
            (CreateCancelCommand() with
            {
                ReplacementStartDate = ReplacementStartDate,
            }, "replacementStartDate"),
            (CreateCancelCommand() with
            {
                ReplacementEndDate = ReplacementEndDate,
            }, "replacementEndDate"),
            (CreateCancelCommand() with
            {
                ReplacementReasonCode = "maintenance",
            }, "replacementReasonCode"),
            (CreateCancelCommand() with
            {
                ReplacementReasonComment = "Boiler replacement",
            }, "replacementReasonComment"),
        };

        foreach (var (command, field) in commands)
        {
            AssertRejected(
                CorrectNonWorkingDayPreparationPolicy.Prepare(command),
                CommandErrorCode.ValidationFailed,
                field);
        }
    }

    private static void AssertRejected(
        CorrectNonWorkingDayPreparationResult result,
        CommandErrorCode code,
        string field)
    {
        Assert.False(result.IsPrepared);
        Assert.Null(result.Preparation);
        var error = Assert.Single(result.Errors);
        Assert.Equal(code, error.Code);
        Assert.Equal(field, error.Field);
        Assert.False(string.IsNullOrWhiteSpace(error.Message));
    }

    private static CorrectNonWorkingDayCommand CreateRangeCommand(
        CommandEnvelope? envelope = null)
    {
        return new CorrectNonWorkingDayCommand(
            envelope ?? CreateEnvelope(),
            PeriodId,
            NonWorkingDayCorrectionMode.ReplaceRange,
            ReplacementStartDate,
            ReplacementEndDate,
            "maintenance",
            "Boiler replacement",
            "bodylife-nwd-correction-v1.range.signature");
    }

    private static CorrectNonWorkingDayCommand CreateReasonCommand(
        CommandEnvelope? envelope = null)
    {
        return new CorrectNonWorkingDayCommand(
            envelope ?? CreateEnvelope(),
            PeriodId,
            NonWorkingDayCorrectionMode.ReplaceReason,
            ReplacementStartDate: null,
            ReplacementEndDate: null,
            ReplacementReasonCode: "weather_closure",
            ReplacementReasonComment: "Corrected explanation",
            ConfirmationToken: "bodylife-nwd-correction-v1.reason.signature");
    }

    private static CorrectNonWorkingDayCommand CreateCancelCommand(
        CommandEnvelope? envelope = null)
    {
        return new CorrectNonWorkingDayCommand(
            envelope ?? CreateEnvelope(),
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
            new RequestCorrelationId("correction-correlation"),
            EntryOrigin.Normal,
            new DateTimeOffset(2026, 7, 17, 14, 0, 0, TimeSpan.Zero),
            "correction-key",
            "Schedule correction",
            "Owner confirmed exact scope");
    }
}
