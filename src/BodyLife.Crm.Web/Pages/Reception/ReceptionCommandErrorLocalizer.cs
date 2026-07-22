using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Web.Localization;
using Microsoft.Extensions.Localization;

namespace BodyLife.Crm.Web.Pages.Reception;

/// <summary>Maps stable command errors to safe, audience-facing Reception wording.</summary>
public static class ReceptionCommandErrorLocalizer
{
    public static string Display(IStringLocalizer<global::BodyLife.Crm.Web.Localization.Reception> localizer, CommandError error) =>
        localizer[Key(error.Code, error.Field)];

    public static string Key(CommandErrorCode code, string? field) => (code, field) switch
    {
        (CommandErrorCode.ValidationFailed, "membershipTypeId") => "Error.Validation.MembershipType",
        (CommandErrorCode.ValidationFailed, "membershipId") => "Error.Validation.Membership",
        (CommandErrorCode.ValidationFailed, "visitKind") => "Error.Validation.VisitKind",
        (CommandErrorCode.ValidationFailed, "reason") => "Error.Validation.Reason",
        (CommandErrorCode.ValidationFailed, "confirmed") => "Error.Validation.Confirmation",
        (CommandErrorCode.ValidationFailed, "startDate") => "Error.Validation.StartDate",
        (CommandErrorCode.ValidationFailed, "negativeHandlingDecision") => "Error.Validation.NegativeDecision",
        (CommandErrorCode.ValidationFailed, "mode") => "Error.Validation.CorrectionMode",
        (CommandErrorCode.ValidationFailed, "paymentContext") or
            (CommandErrorCode.ValidationFailed, "replacement.paymentContext") => "Error.Validation.PaymentContext",
        (CommandErrorCode.ValidationFailed, "occurredAt") or
            (CommandErrorCode.ValidationFailed, "replacement.occurredAt") => "Error.Validation.OccurredAt",
        (CommandErrorCode.ValidationFailed, "occurredAt.localTime") or
            (CommandErrorCode.ValidationFailed, "replacement.occurredAt.localTime") => "Error.Validation.LocalTime",
        (CommandErrorCode.ValidationFailed, "comment") or
            (CommandErrorCode.ValidationFailed, "envelope.comment") or
            (CommandErrorCode.ValidationFailed, "replacement.comment") => "Error.Validation.Comment",
        (CommandErrorCode.ValidationFailed, "originalPaymentId") => "Error.Validation.OriginalPayment",
        (CommandErrorCode.ValidationFailed, "replacement.membershipId") => "Error.Validation.ReplacementMembership",
        (CommandErrorCode.ValidationFailed, "surname") => "Error.Validation.Surname",
        (CommandErrorCode.ValidationFailed, "name") => "Error.Validation.Name",
        (CommandErrorCode.ValidationFailed, "cardNumber") => "Error.Validation.CardNumber",
        (CommandErrorCode.ValidationFailed, var duplicateField)
            when duplicateField?.StartsWith("duplicateWarningAcknowledgements", StringComparison.Ordinal) == true =>
                "Error.DuplicateWarningNotAcknowledged",
        (CommandErrorCode.ValidationFailed, "amount") => "Error.Validation.Amount",
        (CommandErrorCode.ValidationFailed, "payment.amount") => "Error.Validation.Amount",
        (CommandErrorCode.ValidationFailed, "replacement.amount") => "Error.Validation.Amount",
        (CommandErrorCode.ValidationFailed, "range.startDate") => "Error.Validation.StartDate",
        (CommandErrorCode.ValidationFailed, "range.endDate") => "Error.Validation.EndDate",
        (CommandErrorCode.ValidationFailed, "range") => "Error.Validation.DateRange",
        (CommandErrorCode.PermissionDenied, _) => "Error.PermissionDenied",
        (CommandErrorCode.NotFound, "membershipTypeId") => "Error.NotFound.MembershipType",
        (CommandErrorCode.NotFound, "membershipId") => "Error.NotFound.Membership",
        (CommandErrorCode.NotFound, _) => "Error.NotFound",
        (CommandErrorCode.DuplicateSubmission, _) => "Error.DuplicateSubmission",
        (CommandErrorCode.CardNumberAlreadyCurrent, _) => "Error.CardNumberAlreadyCurrent",
        (CommandErrorCode.DuplicateWarningNotAcknowledged, _) => "Error.DuplicateWarningNotAcknowledged",
        (CommandErrorCode.WarningAcknowledgementRequired, _) => "Error.WarningAcknowledgementRequired",
        (CommandErrorCode.ConcurrencyConflict, _) or (CommandErrorCode.StaleState, _) => "Error.StaleState",
        (CommandErrorCode.ReasonRequired, _) => "Error.ReasonRequired",
        (CommandErrorCode.AlreadyCanceled, _) => "Error.AlreadyCanceled",
        (CommandErrorCode.DayClosedRequiresOwner, _) => "Error.DayClosedRequiresOwner",
        (CommandErrorCode.MembershipTypeInactive, _) => "Error.MembershipTypeInactive",
        (CommandErrorCode.MembershipNotEligible, _) => "Error.MembershipNotEligible",
        (CommandErrorCode.NegativeDecisionRequired, _) => "Error.NegativeDecisionRequired",
        (CommandErrorCode.FreezeConflictsWithVisit, _) => "Error.FreezeConflictsWithVisit",
        (CommandErrorCode.VisitDuringFreeze, _) => "Error.VisitDuringFreeze",
        (CommandErrorCode.PreviewExpired, _) => "Error.PreviewExpired",
        (CommandErrorCode.AffectedScopeChanged, _) => "Error.AffectedScopeChanged",
        (CommandErrorCode.RecalculationFailed, _) => "Error.RecalculationFailed",
        _ => "Error.Generic",
    };
}
