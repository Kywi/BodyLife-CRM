using System.Text.Json;
using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.Modules.Payments;
using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Infrastructure.Persistence.Payments;

internal static class PaymentQuerySupport
{
    private const string ActiveStatus = "active";
    private const string CanceledStatus = "canceled";
    private const string ReplacedStatus = "replaced";

    internal static Task<bool> IsActorAuthorizedAsync(
        BodyLifeDbContext dbContext,
        ActorContext? actor,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        return actor is not null && PaymentCommandSupport.IsAllowedActorShape(actor)
            ? PaymentCommandSupport.IsCanonicalActorAuthorizedAsync(
                dbContext,
                actor,
                now,
                cancellationToken)
            : Task.FromResult(false);
    }

    internal static QueryPermissionSet BuildCorrectionPermissions(
        ActorContext actor,
        ClientPaymentRowStatus paymentStatus,
        PaymentContext paymentContext,
        PaymentDayReconciliationStatus dayStatus)
    {
        if (paymentStatus != ClientPaymentRowStatus.Active
            || paymentContext == PaymentContext.NegativeClosure)
        {
            return QueryPermissionSet.Empty;
        }

        return dayStatus switch
        {
            PaymentDayReconciliationStatus.Open => new QueryPermissionSet(
            [
                QueryPermissionResult.Allowed(
                    PaymentActionKeys.Correct,
                    PaymentActionKeys.AdminOrOwnerPolicy),
            ]),
            PaymentDayReconciliationStatus.Reconciled when actor.Role == ActorRole.Owner
                => new QueryPermissionSet(
                [
                    QueryPermissionResult.Allowed(
                        PaymentActionKeys.Correct,
                        PaymentActionKeys.OwnerPolicy),
                ]),
            PaymentDayReconciliationStatus.Reconciled => new QueryPermissionSet(
            [
                QueryPermissionResult.Denied(
                    PaymentActionKeys.Correct,
                    PaymentActionKeys.OwnerPolicy,
                    "day_closed_requires_owner",
                    "Only the Owner can correct a Payment from a reconciled day."),
            ]),
            _ => throw new ArgumentOutOfRangeException(
                nameof(dayStatus),
                dayStatus,
                null),
        };
    }

    internal static bool TryMapSourceRow(
        CanonicalPaymentSourceRow source,
        CanonicalPaymentCancellationSourceRow? cancellationSource,
        CanonicalPaymentCorrectionSourceRow? correctionFromOriginalSource,
        CanonicalPaymentCorrectionSourceRow? correctionToReplacementSource,
        out CanonicalPaymentProjection? projection)
    {
        projection = null;
        if (source.Amount <= 0
            || !IsCanonicalCurrency(source.Currency)
            || !TryMapPaymentMethod(source.Method, out var method)
            || !TryMapPaymentContext(source.PaymentContext, out var paymentContext)
            || !TryMapEntryOrigin(source.EntryOrigin, out var entryOrigin)
            || !HasCanonicalMembershipSnapshot(source))
        {
            return false;
        }

        if (!TryMapCancellation(
                source,
                cancellationSource,
                out var cancellation)
            || !TryMapCorrection(
                source,
                correctionFromOriginalSource,
                isIncoming: true,
                out var correctionFromOriginal)
            || !TryMapCorrection(
                source,
                correctionToReplacementSource,
                isIncoming: false,
                out var correctionToReplacement)
            || !TryMapStatus(
                source.Status,
                cancellation is not null,
                correctionToReplacement is not null,
                out var status))
        {
            return false;
        }

        projection = new CanonicalPaymentProjection(
            new Money(source.Amount, source.Currency),
            method,
            paymentContext,
            entryOrigin,
            status,
            cancellation,
            correctionFromOriginal,
            correctionToReplacement);
        return true;
    }

    private static bool IsCanonicalCurrency(string value)
    {
        return !string.IsNullOrWhiteSpace(value)
            && value == value.Trim()
            && value == value.ToUpperInvariant();
    }

    private static bool HasCanonicalMembershipSnapshot(
        CanonicalPaymentSourceRow source)
    {
        return source.MembershipId switch
        {
            null => source.MembershipClientId is null
                && source.MembershipTypeNameSnapshot is null,
            not null => source.MembershipClientId == source.ClientId
                && !string.IsNullOrWhiteSpace(
                    source.MembershipTypeNameSnapshot),
        };
    }

    private static bool TryMapPaymentMethod(
        string value,
        out PaymentMethod method)
    {
        method = value switch
        {
            "cash" => PaymentMethod.Cash,
            _ => default,
        };

        return method != default;
    }

    private static bool TryMapPaymentContext(
        string value,
        out PaymentContext paymentContext)
    {
        paymentContext = value switch
        {
            "membership_sale" => PaymentContext.MembershipSale,
            "one_off" => PaymentContext.OneOff,
            "trial" => PaymentContext.Trial,
            "negative_closure" => PaymentContext.NegativeClosure,
            "other" => PaymentContext.Other,
            _ => default,
        };

        return paymentContext != default;
    }

    private static bool TryMapEntryOrigin(
        string value,
        out EntryOrigin entryOrigin)
    {
        entryOrigin = value switch
        {
            "normal" => EntryOrigin.Normal,
            "manual_backfill" => EntryOrigin.ManualBackfill,
            "paper_fallback" => EntryOrigin.PaperFallback,
            "future_import" => EntryOrigin.FutureImport,
            _ => default,
        };

        return entryOrigin != default;
    }

    private static bool TryMapStatus(
        string value,
        bool hasCancellation,
        bool hasCorrectionToReplacement,
        out ClientPaymentRowStatus status)
    {
        status = (value, hasCancellation, hasCorrectionToReplacement) switch
        {
            (ActiveStatus, false, false) => ClientPaymentRowStatus.Active,
            (CanceledStatus, true, false) => ClientPaymentRowStatus.Canceled,
            (ReplacedStatus, false, true) => ClientPaymentRowStatus.Replaced,
            _ => default,
        };

        return status != default;
    }

    private static bool TryMapCancellation(
        CanonicalPaymentSourceRow source,
        CanonicalPaymentCancellationSourceRow? cancellationSource,
        out ClientPaymentCancellation? cancellation)
    {
        cancellation = null;
        if (cancellationSource is null)
        {
            return true;
        }

        if (cancellationSource.PaymentId != source.PaymentId
            || string.IsNullOrWhiteSpace(cancellationSource.Reason)
            || !TryMapEntryOrigin(
                cancellationSource.EntryOrigin,
                out var entryOrigin))
        {
            return false;
        }

        cancellation = new ClientPaymentCancellation(
            cancellationSource.CancellationId,
            cancellationSource.Reason,
            cancellationSource.OccurredAt,
            cancellationSource.RecordedAt,
            cancellationSource.RecordedByAccountId,
            cancellationSource.SessionId,
            entryOrigin,
            cancellationSource.EntryBatchId);
        return true;
    }

    private static bool TryMapCorrection(
        CanonicalPaymentSourceRow source,
        CanonicalPaymentCorrectionSourceRow? correctionSource,
        bool isIncoming,
        out ClientPaymentCorrection? correction)
    {
        correction = null;
        if (correctionSource is null)
        {
            return true;
        }

        var referencesSource = isIncoming
            ? correctionSource.ReplacementPaymentId == source.PaymentId
            : correctionSource.OriginalPaymentId == source.PaymentId;
        if (!referencesSource
            || correctionSource.ClientId != source.ClientId
            || correctionSource.OriginalPaymentId == Guid.Empty
            || correctionSource.ReplacementPaymentId == Guid.Empty
            || correctionSource.OriginalPaymentId
                == correctionSource.ReplacementPaymentId
            || string.IsNullOrWhiteSpace(correctionSource.Reason)
            || !TryParseChangedFields(
                correctionSource.ChangedFieldsJson,
                out var changedFields)
            || !TryMapEntryOrigin(
                correctionSource.EntryOrigin,
                out var entryOrigin))
        {
            return false;
        }

        correction = new ClientPaymentCorrection(
            correctionSource.CorrectionId,
            correctionSource.OriginalPaymentId,
            correctionSource.ReplacementPaymentId,
            changedFields,
            correctionSource.Reason,
            correctionSource.OccurredAt,
            correctionSource.RecordedAt,
            correctionSource.RecordedByAccountId,
            correctionSource.SessionId,
            entryOrigin,
            correctionSource.EntryBatchId);
        return true;
    }

    private static bool TryParseChangedFields(
        string value,
        out IReadOnlyList<string> changedFields)
    {
        changedFields = [];
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(value);
            if (document.RootElement.ValueKind != JsonValueKind.Array
                || document.RootElement.GetArrayLength() == 0)
            {
                return false;
            }

            var values = new List<string>();
            var uniqueValues = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in document.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String)
                {
                    return false;
                }

                var field = item.GetString();
                if (string.IsNullOrWhiteSpace(field)
                    || field != field.Trim()
                    || !uniqueValues.Add(field))
                {
                    return false;
                }

                values.Add(field);
            }

            changedFields = values.AsReadOnly();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    internal sealed record CanonicalPaymentSourceRow(
        Guid PaymentId,
        Guid ClientId,
        Guid? MembershipId,
        Guid? MembershipClientId,
        string? MembershipTypeNameSnapshot,
        decimal Amount,
        string Currency,
        string Method,
        string PaymentContext,
        DateTimeOffset OccurredAt,
        DateTimeOffset RecordedAt,
        Guid RecordedByAccountId,
        Guid SessionId,
        string EntryOrigin,
        Guid? EntryBatchId,
        string? Comment,
        string Status);

    internal sealed record CanonicalPaymentCancellationSourceRow(
        Guid CancellationId,
        Guid PaymentId,
        string Reason,
        DateTimeOffset OccurredAt,
        DateTimeOffset RecordedAt,
        Guid RecordedByAccountId,
        Guid SessionId,
        string EntryOrigin,
        Guid? EntryBatchId);

    internal sealed record CanonicalPaymentCorrectionSourceRow(
        Guid CorrectionId,
        Guid ClientId,
        Guid OriginalPaymentId,
        Guid ReplacementPaymentId,
        string ChangedFieldsJson,
        string Reason,
        DateTimeOffset OccurredAt,
        DateTimeOffset RecordedAt,
        Guid RecordedByAccountId,
        Guid SessionId,
        string EntryOrigin,
        Guid? EntryBatchId);

    internal sealed record CanonicalPaymentProjection(
        Money Amount,
        PaymentMethod Method,
        PaymentContext PaymentContext,
        EntryOrigin EntryOrigin,
        ClientPaymentRowStatus Status,
        ClientPaymentCancellation? Cancellation,
        ClientPaymentCorrection? CorrectionFromOriginal,
        ClientPaymentCorrection? CorrectionToReplacement);
}
