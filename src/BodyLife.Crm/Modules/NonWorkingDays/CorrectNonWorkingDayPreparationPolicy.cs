using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Modules.NonWorkingDays;

public static class CorrectNonWorkingDayPreparationPolicy
{
    private const int IdempotencyKeyMaxLength = 200;
    private const int CorrelationIdMaxLength = 128;
    private const int DeviceLabelMaxLength = 120;
    private const int EnvelopeTextMaxLength = 1000;
    private static readonly DateRange ReasonValidationPeriod = new(
        new DateOnly(2000, 1, 1),
        new DateOnly(2000, 1, 1));

    public static CorrectNonWorkingDayPreparationResult Prepare(
        CorrectNonWorkingDayCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.PeriodId == Guid.Empty)
        {
            return Invalid(
                "NonWorkingDay period id is required.",
                "periodId");
        }

        if (!Enum.IsDefined(command.Mode))
        {
            return Invalid(
                "Correction mode is not supported.",
                "mode");
        }

        var envelopeValidation = TryPrepareEnvelope(
            command.Envelope,
            out var canonicalEnvelope);
        if (envelopeValidation is not null)
        {
            return envelopeValidation;
        }

        if (string.IsNullOrWhiteSpace(command.ConfirmationToken)
            || command.ConfirmationToken != command.ConfirmationToken.Trim()
            || command.ConfirmationToken.Length
                > NonWorkingDayCorrectionConfirmation.MaxTokenLength)
        {
            return Invalid(
                "A canonical correction confirmation token is required.",
                "confirmationToken");
        }

        var replacementValidation = TryPrepareReplacement(
            command,
            out var replacementPeriod,
            out var replacementReasonCode,
            out var replacementReasonComment);
        if (replacementValidation is not null)
        {
            return replacementValidation;
        }

        return CorrectNonWorkingDayPreparationResult.Prepared(
            new CorrectNonWorkingDayPreparation(
                canonicalEnvelope!,
                command.PeriodId,
                command.Mode,
                replacementPeriod,
                replacementReasonCode,
                replacementReasonComment,
                command.ConfirmationToken));
    }

    private static CorrectNonWorkingDayPreparationResult? TryPrepareEnvelope(
        CommandEnvelope? envelope,
        out CommandEnvelope? canonicalEnvelope)
    {
        canonicalEnvelope = null;
        if (envelope is null)
        {
            return Invalid("Command envelope is required.", "envelope");
        }

        if (envelope.Actor is null)
        {
            return Invalid("Actor context is required.", "actor");
        }

        if (envelope.Actor.AccountId.Value == Guid.Empty)
        {
            return Invalid("Actor account id is required.", "actorAccountId");
        }

        if (!Enum.IsDefined(envelope.Actor.Role))
        {
            return Invalid("Actor role is not supported.", "actorRole");
        }

        if (!Enum.IsDefined(envelope.Actor.AccountKind))
        {
            return Invalid("Account kind is not supported.", "accountKind");
        }

        if (envelope.Actor.SessionId.Value == Guid.Empty)
        {
            return Invalid("Actor session id is required.", "sessionId");
        }

        var idempotencyKey = NormalizeOptional(envelope.IdempotencyKey);
        if (idempotencyKey is null || idempotencyKey.Length > IdempotencyKeyMaxLength)
        {
            return Invalid(
                $"Idempotency key is required and must be {IdempotencyKeyMaxLength} characters or fewer.",
                "idempotencyKey");
        }

        var requestCorrelationId = NormalizeOptional(
            envelope.RequestCorrelationId.Value);
        if (requestCorrelationId is null
            || requestCorrelationId.Length > CorrelationIdMaxLength)
        {
            return Invalid(
                $"Request correlation id is required and must be {CorrelationIdMaxLength} characters or fewer.",
                "requestCorrelationId");
        }

        var deviceLabel = NormalizeOptional(envelope.Actor.DeviceLabel);
        if (deviceLabel?.Length > DeviceLabelMaxLength)
        {
            return Invalid(
                $"Device label must be {DeviceLabelMaxLength} characters or fewer.",
                "deviceLabel");
        }

        if (!Enum.IsDefined(envelope.EntryOrigin))
        {
            return Invalid("Entry origin is not supported.", "entryOrigin");
        }

        if (envelope.OccurredAt is null)
        {
            return Invalid(
                "Occurred_at is required for NonWorkingDay correction.",
                "occurredAt");
        }

        if (!BusinessTimeZone.TryNormalizeUtcInstant(envelope.OccurredAt.Value, out var occurredAt))
        {
            return Invalid(
                "Occurred_at is outside the supported business-calendar range.",
                "occurredAt");
        }

        var reason = NormalizeOptional(envelope.Reason);
        if (reason is null)
        {
            return CorrectNonWorkingDayPreparationResult.Rejected(
                CommandErrorCode.ReasonRequired,
                "Reason is required for NonWorkingDay correction.",
                "reason");
        }

        if (reason.Length > EnvelopeTextMaxLength)
        {
            return Invalid(
                $"Correction reason must be {EnvelopeTextMaxLength} characters or fewer.",
                "reason");
        }

        var comment = NormalizeOptional(envelope.Comment);
        if (comment is null)
        {
            return CorrectNonWorkingDayPreparationResult.Rejected(
                CommandErrorCode.ReasonRequired,
                "Comment is required for NonWorkingDay correction.",
                "comment");
        }

        if (comment.Length > EnvelopeTextMaxLength)
        {
            return Invalid(
                $"Correction comment must be {EnvelopeTextMaxLength} characters or fewer.",
                "comment");
        }

        canonicalEnvelope = new CommandEnvelope(
            envelope.Actor with { DeviceLabel = deviceLabel },
            new RequestCorrelationId(requestCorrelationId),
            envelope.EntryOrigin,
            occurredAt,
            idempotencyKey,
            reason,
            comment);
        return null;
    }

    private static CorrectNonWorkingDayPreparationResult? TryPrepareReplacement(
        CorrectNonWorkingDayCommand command,
        out DateRange? replacementPeriod,
        out string? replacementReasonCode,
        out string? replacementReasonComment)
    {
        replacementPeriod = null;
        replacementReasonCode = null;
        replacementReasonComment = null;

        return command.Mode switch
        {
            NonWorkingDayCorrectionMode.ReplaceRange => TryPrepareRangeReplacement(
                command,
                out replacementPeriod,
                out replacementReasonCode,
                out replacementReasonComment),
            NonWorkingDayCorrectionMode.ReplaceReason => TryPrepareReasonReplacement(
                command,
                out replacementReasonCode,
                out replacementReasonComment),
            NonWorkingDayCorrectionMode.Cancel => ValidateCancelShape(command),
            _ => throw new ArgumentOutOfRangeException(
                nameof(command),
                command.Mode,
                "Correction mode is not supported."),
        };
    }

    private static CorrectNonWorkingDayPreparationResult? TryPrepareRangeReplacement(
        CorrectNonWorkingDayCommand command,
        out DateRange? replacementPeriod,
        out string? replacementReasonCode,
        out string? replacementReasonComment)
    {
        replacementPeriod = null;
        replacementReasonCode = null;
        replacementReasonComment = null;

        if (command.ReplacementStartDate is null
            || command.ReplacementStartDate == default(DateOnly))
        {
            return Invalid(
                "Replacement start date is required.",
                "replacementStartDate");
        }

        if (command.ReplacementEndDate is null
            || command.ReplacementEndDate == default(DateOnly))
        {
            return Invalid(
                "Replacement end date is required.",
                "replacementEndDate");
        }

        if (command.ReplacementEndDate < command.ReplacementStartDate)
        {
            return Invalid(
                "Replacement end date must be on or after the start date.",
                "replacementEndDate");
        }

        replacementPeriod = new DateRange(
            command.ReplacementStartDate.Value,
            command.ReplacementEndDate.Value);
        return TryPrepareReplacementReason(
            replacementPeriod.Value,
            command.ReplacementReasonCode,
            command.ReplacementReasonComment,
            out replacementReasonCode,
            out replacementReasonComment);
    }

    private static CorrectNonWorkingDayPreparationResult? TryPrepareReasonReplacement(
        CorrectNonWorkingDayCommand command,
        out string? replacementReasonCode,
        out string? replacementReasonComment)
    {
        replacementReasonCode = null;
        replacementReasonComment = null;

        if (command.ReplacementStartDate is not null)
        {
            return Invalid(
                "Reason-only replacement must preserve the original start date.",
                "replacementStartDate");
        }

        if (command.ReplacementEndDate is not null)
        {
            return Invalid(
                "Reason-only replacement must preserve the original end date.",
                "replacementEndDate");
        }

        return TryPrepareReplacementReason(
            ReasonValidationPeriod,
            command.ReplacementReasonCode,
            command.ReplacementReasonComment,
            out replacementReasonCode,
            out replacementReasonComment);
    }

    private static CorrectNonWorkingDayPreparationResult? ValidateCancelShape(
        CorrectNonWorkingDayCommand command)
    {
        if (command.ReplacementStartDate is not null)
        {
            return Invalid(
                "Cancel mode cannot include a replacement start date.",
                "replacementStartDate");
        }

        if (command.ReplacementEndDate is not null)
        {
            return Invalid(
                "Cancel mode cannot include a replacement end date.",
                "replacementEndDate");
        }

        if (NormalizeOptional(command.ReplacementReasonCode) is not null)
        {
            return Invalid(
                "Cancel mode cannot include a replacement reason code.",
                "replacementReasonCode");
        }

        if (NormalizeOptional(command.ReplacementReasonComment) is not null)
        {
            return Invalid(
                "Cancel mode cannot include a replacement reason comment.",
                "replacementReasonComment");
        }

        return null;
    }

    private static CorrectNonWorkingDayPreparationResult? TryPrepareReplacementReason(
        DateRange validationPeriod,
        string? reasonCode,
        string? reasonComment,
        out string? canonicalReasonCode,
        out string? canonicalReasonComment)
    {
        canonicalReasonCode = null;
        canonicalReasonComment = null;

        if (string.IsNullOrWhiteSpace(reasonCode))
        {
            return Invalid(
                "Replacement reason code is required.",
                "replacementReasonCode");
        }

        try
        {
            var input = new NonWorkingDayPreviewInput(
                validationPeriod,
                reasonCode,
                reasonComment);
            canonicalReasonCode = input.ReasonCode;
            canonicalReasonComment = input.ReasonComment;
            return null;
        }
        catch (ArgumentException exception)
            when (exception.ParamName == "reasonCode")
        {
            return Invalid(
                $"Replacement reason code must be {NonWorkingDayPreviewInput.ReasonCodeMaxLength} characters or fewer.",
                "replacementReasonCode");
        }
        catch (ArgumentException exception)
            when (exception.ParamName == "reasonComment")
        {
            return Invalid(
                $"Replacement reason comment must be {NonWorkingDayPreviewInput.ReasonCommentMaxLength} characters or fewer.",
                "replacementReasonComment");
        }
    }

    private static CorrectNonWorkingDayPreparationResult Invalid(
        string message,
        string? field)
    {
        return CorrectNonWorkingDayPreparationResult.Rejected(
            CommandErrorCode.ValidationFailed,
            message,
            field);
    }

    private static string? NormalizeOptional(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }
}
