using BodyLife.Crm.Infrastructure.Persistence.ClientsSearch;
using BodyLife.Crm.Infrastructure.Persistence.Freezes;
using BodyLife.Crm.Infrastructure.Persistence.Memberships;
using BodyLife.Crm.Infrastructure.Persistence.MembershipTypes;
using BodyLife.Crm.Infrastructure.Persistence.NonWorkingDays;
using BodyLife.Crm.Infrastructure.Persistence.Payments;
using BodyLife.Crm.Infrastructure.Persistence.UsersRoles;
using BodyLife.Crm.Infrastructure.Persistence.Visits;

namespace BodyLife.Crm.Infrastructure.Persistence.Audit;

internal static class BusinessAuditEventMatrix
{
    private const RequiredPayloadFields CreatesCommandFact =
        RequiredPayloadFields.AfterSummary | RequiredPayloadFields.IdempotencyKey;
    private const RequiredPayloadFields ChangesCommandFact =
        RequiredPayloadFields.BeforeSummary
        | RequiredPayloadFields.AfterSummary
        | RequiredPayloadFields.IdempotencyKey;
    private const RequiredPayloadFields CorrectsRelatedCommandFact =
        RequiredPayloadFields.RelatedEntityRefs
        | RequiredPayloadFields.BeforeSummary
        | RequiredPayloadFields.AfterSummary
        | RequiredPayloadFields.Explanation
        | RequiredPayloadFields.IdempotencyKey;

    private static readonly IReadOnlyDictionary<string, EventRequirements> RequirementsByAction =
        new Dictionary<string, EventRequirements>(StringComparer.Ordinal)
        {
            [ClientAuditActions.Created] = new(ClientAuditActions.EntityType, CreatesCommandFact),
            [ClientAuditActions.Updated] = new(ClientAuditActions.EntityType, ChangesCommandFact),
            [ClientAuditActions.CardAssigned] = new(
                ClientAuditActions.EntityType,
                RequiredPayloadFields.RelatedEntityRefs
                | RequiredPayloadFields.AfterSummary
                | RequiredPayloadFields.IdempotencyKey),
            [ClientAuditActions.CardChanged] = new(
                ClientAuditActions.EntityType,
                CorrectsRelatedCommandFact),
            [ClientAuditActions.CardCleared] = new(
                ClientAuditActions.EntityType,
                RequiredPayloadFields.RelatedEntityRefs
                | RequiredPayloadFields.BeforeSummary
                | RequiredPayloadFields.Explanation
                | RequiredPayloadFields.IdempotencyKey),
            [MembershipTypeAuditActions.Created] = new(
                MembershipTypeAuditActions.EntityType,
                CreatesCommandFact),
            [MembershipTypeAuditActions.Edited] = new(
                MembershipTypeAuditActions.EntityType,
                ChangesCommandFact),
            [MembershipTypeAuditActions.Deactivated] = new(
                MembershipTypeAuditActions.EntityType,
                ChangesCommandFact | RequiredPayloadFields.Explanation),
            [MembershipAuditActions.Issued] = new(
                MembershipAuditActions.MembershipEntityType,
                RequiredPayloadFields.RelatedEntityRefs | CreatesCommandFact),
            [MembershipAuditActions.OpeningStateCreated] = new(
                MembershipAuditActions.OpeningStateEntityType,
                RequiredPayloadFields.RelatedEntityRefs
                | CreatesCommandFact
                | RequiredPayloadFields.Explanation),
            [VisitAuditActions.Marked] = new(
                VisitAuditActions.VisitEntityType,
                RequiredPayloadFields.RelatedEntityRefs | CreatesCommandFact),
            [VisitAuditActions.Canceled] = new(
                VisitAuditActions.VisitEntityType,
                CorrectsRelatedCommandFact),
            [PaymentAuditActions.Created] = new(
                PaymentAuditActions.EntityType,
                RequiredPayloadFields.RelatedEntityRefs | CreatesCommandFact),
            [PaymentAuditActions.Corrected] = new(
                PaymentAuditActions.EntityType,
                CorrectsRelatedCommandFact),
            [PaymentAuditActions.Canceled] = new(
                PaymentAuditActions.EntityType,
                CorrectsRelatedCommandFact),
            [FreezeAuditActions.Added] = new(
                FreezeAuditActions.FreezeEntityType,
                CorrectsRelatedCommandFact),
            [FreezeAuditActions.Canceled] = new(
                FreezeAuditActions.FreezeEntityType,
                CorrectsRelatedCommandFact),
            [NonWorkingDayAuditActions.Added] = new(
                NonWorkingDayAuditActions.PeriodEntityType,
                CorrectsRelatedCommandFact),
            [NonWorkingDayAuditActions.Corrected] = new(
                NonWorkingDayAuditActions.PeriodEntityType,
                CorrectsRelatedCommandFact),
            [NonWorkingDayAuditActions.Canceled] = new(
                NonWorkingDayAuditActions.PeriodEntityType,
                CorrectsRelatedCommandFact),
            [StaffAccountAuditActions.Created] = new(
                StaffAccountAuditActions.EntityType,
                RequiredPayloadFields.AfterSummary),
            [StaffAccountAuditActions.DisplayNameUpdated] = new(
                StaffAccountAuditActions.EntityType,
                RequiredPayloadFields.BeforeSummary | RequiredPayloadFields.AfterSummary),
            [StaffAccountAuditActions.Activated] = new(
                StaffAccountAuditActions.EntityType,
                RequiredPayloadFields.BeforeSummary | RequiredPayloadFields.AfterSummary),
            [StaffAccountAuditActions.Deactivated] = new(
                StaffAccountAuditActions.EntityType,
                RequiredPayloadFields.BeforeSummary
                | RequiredPayloadFields.AfterSummary
                | RequiredPayloadFields.Explanation),
            [StaffAccountAuditActions.CredentialsConfigured] = new(
                StaffAccountAuditActions.EntityType,
                RequiredPayloadFields.BeforeSummary | RequiredPayloadFields.AfterSummary),
            [StaffAccountAuditActions.CredentialsReset] = new(
                StaffAccountAuditActions.EntityType,
                RequiredPayloadFields.BeforeSummary
                | RequiredPayloadFields.AfterSummary
                | RequiredPayloadFields.Explanation),
        };

    public static void Validate(
        string actionType,
        string entityType,
        string relatedEntityRefsJson,
        string beforeSummaryJson,
        string afterSummaryJson,
        string? reason,
        string? comment,
        string? idempotencyKey)
    {
        if (!RequirementsByAction.TryGetValue(actionType, out var requirements))
        {
            throw new ArgumentException(
                $"Audit action type '{actionType}' is not registered in the canonical matrix.",
                nameof(actionType));
        }

        if (!string.Equals(entityType, requirements.EntityType, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Audit action '{actionType}' requires entity type '{requirements.EntityType}'.",
                nameof(entityType));
        }

        RequireJsonPayload(
            requirements,
            RequiredPayloadFields.RelatedEntityRefs,
            relatedEntityRefsJson,
            "relatedEntityRefs",
            actionType);
        RequireJsonPayload(
            requirements,
            RequiredPayloadFields.BeforeSummary,
            beforeSummaryJson,
            "beforeSummary",
            actionType);
        RequireJsonPayload(
            requirements,
            RequiredPayloadFields.AfterSummary,
            afterSummaryJson,
            "afterSummary",
            actionType);
        RequireExplanation(
            requirements,
            reason,
            comment,
            actionType);
        RequireTextPayload(
            requirements,
            RequiredPayloadFields.IdempotencyKey,
            idempotencyKey,
            "idempotencyKey",
            actionType);
    }

    private static void RequireExplanation(
        EventRequirements requirements,
        string? reason,
        string? comment,
        string actionType)
    {
        if (requirements.RequiredFields.HasFlag(RequiredPayloadFields.Explanation)
            && string.IsNullOrWhiteSpace(reason)
            && string.IsNullOrWhiteSpace(comment))
        {
            throw new ArgumentException(
                $"Audit action '{actionType}' requires a reason or comment.",
                "reason");
        }
    }

    private static void RequireJsonPayload(
        EventRequirements requirements,
        RequiredPayloadFields field,
        string json,
        string parameterName,
        string actionType)
    {
        if (requirements.RequiredFields.HasFlag(field)
            && string.Equals(json, "{}", StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Audit action '{actionType}' requires {parameterName}.",
                parameterName);
        }
    }

    private static void RequireTextPayload(
        EventRequirements requirements,
        RequiredPayloadFields field,
        string? value,
        string parameterName,
        string actionType)
    {
        if (requirements.RequiredFields.HasFlag(field) && string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(
                $"Audit action '{actionType}' requires {parameterName}.",
                parameterName);
        }
    }

    private sealed record EventRequirements(
        string EntityType,
        RequiredPayloadFields RequiredFields);

    [Flags]
    private enum RequiredPayloadFields
    {
        None = 0,
        RelatedEntityRefs = 1 << 0,
        BeforeSummary = 1 << 1,
        AfterSummary = 1 << 2,
        Explanation = 1 << 3,
        IdempotencyKey = 1 << 4,
    }
}
