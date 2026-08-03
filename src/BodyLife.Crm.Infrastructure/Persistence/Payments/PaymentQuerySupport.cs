using System.Text.Json;
using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.Infrastructure.Persistence.Memberships;
using BodyLife.Crm.Modules.Payments;
using BodyLife.Crm.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BodyLife.Crm.Infrastructure.Persistence.Payments;

internal static class PaymentQuerySupport
{
    private const string ActiveStatus = "active";
    private const string CanceledStatus = "canceled";
    private const string MembershipSaleChangedFieldsJson =
        "[\"membership_sale\"]";
    private const string NegativeCoverageChangedFieldsJson =
        "[\"negative_coverage\"]";
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

    internal static async Task<CanonicalPaymentRelations?>
        ReadCanonicalRelationsAsync(
            BodyLifeDbContext dbContext,
            IReadOnlyCollection<Guid> seedPaymentIds,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(seedPaymentIds);

        if (seedPaymentIds.Any(id => id == Guid.Empty))
        {
            return null;
        }

        var relevantPaymentIds = seedPaymentIds.ToHashSet();
        if (relevantPaymentIds.Count == 0)
        {
            return CanonicalPaymentRelations.Empty;
        }

        var genericCorrections = new Dictionary<Guid, CanonicalPaymentCorrectionSourceRow>();
        var saleCorrections = new Dictionary<Guid, CanonicalPaymentCorrectionSourceRow>();
        var saleCancellations = new Dictionary<Guid, CanonicalPaymentCancellationSourceRow>();
        var negativeCorrections = new Dictionary<Guid, CanonicalPaymentCorrectionSourceRow>();
        var negativeCancellations = new Dictionary<Guid, CanonicalPaymentCancellationSourceRow>();
        var previousPaymentCount = -1;

        while (previousPaymentCount != relevantPaymentIds.Count)
        {
            previousPaymentCount = relevantPaymentIds.Count;
            var lookupPaymentIds = relevantPaymentIds.ToArray();
            var genericRows = await dbContext.Set<PaymentCorrectionRecord>()
                .AsNoTracking()
                .Where(correction =>
                    lookupPaymentIds.Contains(correction.OriginalPaymentId)
                    || lookupPaymentIds.Contains(correction.ReplacementPaymentId))
                .Select(correction => new CanonicalPaymentCorrectionSourceRow(
                    correction.Id,
                    correction.ClientId,
                    correction.OriginalPaymentId,
                    correction.ReplacementPaymentId,
                    correction.ChangedFieldsJson,
                    correction.Reason,
                    correction.OccurredAt,
                    correction.RecordedAt,
                    correction.RecordedByAccountId,
                    correction.SessionId,
                    correction.EntryOrigin,
                    correction.EntryBatchId))
                .ToArrayAsync(cancellationToken);
            foreach (var row in genericRows)
            {
                genericCorrections[row.CorrectionId] = row;
                relevantPaymentIds.Add(row.OriginalPaymentId);
                relevantPaymentIds.Add(row.ReplacementPaymentId);
            }

            var issuedSaleRows = await dbContext
                .Set<IssuedMembershipSaleCorrectionRecord>()
                .AsNoTracking()
                .Where(correction =>
                    lookupPaymentIds.Contains(correction.OriginalPaymentId)
                    || correction.ReplacementPaymentId != null
                        && lookupPaymentIds.Contains(
                            correction.ReplacementPaymentId.Value))
                .ToArrayAsync(cancellationToken);
            foreach (var correction in issuedSaleRows)
            {
                if (correction.Status != ActiveStatus
                    || string.IsNullOrWhiteSpace(correction.Reason))
                {
                    return null;
                }

                if (correction.CorrectionMode == "cancel")
                {
                    if (correction.ReplacementMembershipId is not null
                        || correction.ReplacementPaymentId is not null)
                    {
                        return null;
                    }

                    saleCancellations[correction.Id] =
                        new CanonicalPaymentCancellationSourceRow(
                            correction.Id,
                            correction.OriginalPaymentId,
                            correction.Reason,
                            correction.OccurredAt,
                            correction.RecordedAt,
                            correction.RecordedByAccountId,
                            correction.SessionId,
                            correction.EntryOrigin,
                            EntryBatchId: null);
                    relevantPaymentIds.Add(correction.OriginalPaymentId);
                    continue;
                }

                if (correction.CorrectionMode != "replace"
                    || correction.ReplacementMembershipId is null
                    || correction.ReplacementPaymentId is not { } replacementPaymentId)
                {
                    return null;
                }

                saleCorrections[correction.Id] =
                    new CanonicalPaymentCorrectionSourceRow(
                        correction.Id,
                        correction.ClientId,
                        correction.OriginalPaymentId,
                        replacementPaymentId,
                        MembershipSaleChangedFieldsJson,
                        correction.Reason,
                        correction.OccurredAt,
                        correction.RecordedAt,
                        correction.RecordedByAccountId,
                        correction.SessionId,
                        correction.EntryOrigin,
                        EntryBatchId: null);
                relevantPaymentIds.Add(correction.OriginalPaymentId);
                relevantPaymentIds.Add(replacementPaymentId);
            }

            var negativeClosureIds = await dbContext.Set<PaymentRecord>()
                .AsNoTracking()
                .Where(payment =>
                    lookupPaymentIds.Contains(payment.Id)
                    && payment.NegativeClosureId != null)
                .Select(payment => payment.NegativeClosureId!.Value)
                .Distinct()
                .ToArrayAsync(cancellationToken);
            if (negativeClosureIds.Length == 0)
            {
                continue;
            }

            var closureCorrections = await dbContext
                .Set<MembershipNegativeClosureCorrectionRecord>()
                .AsNoTracking()
                .Where(correction =>
                    negativeClosureIds.Contains(correction.OriginalClosureId)
                    || (correction.ReplacementClosureId != null
                        && negativeClosureIds.Contains(
                            correction.ReplacementClosureId.Value)))
                .ToArrayAsync(cancellationToken);
            var relatedClosureIds = closureCorrections
                .SelectMany(correction => correction.ReplacementClosureId is { } replacementId
                    ? new[] { correction.OriginalClosureId, replacementId }
                    : [correction.OriginalClosureId])
                .Distinct()
                .ToArray();
            if (relatedClosureIds.Length == 0)
            {
                continue;
            }

            var closurePayments = await dbContext.Set<PaymentRecord>()
                .AsNoTracking()
                .Where(payment =>
                    payment.NegativeClosureId != null
                    && relatedClosureIds.Contains(payment.NegativeClosureId.Value))
                .Select(payment => new NegativeClosurePaymentRelationRow(
                    payment.Id,
                    payment.ClientId,
                    payment.NegativeClosureId!.Value,
                    payment.PaymentContext))
                .ToArrayAsync(cancellationToken);
            if (closurePayments
                .GroupBy(payment => payment.NegativeClosureId)
                .Any(group => group.Count() > 1))
            {
                return null;
            }

            var paymentByClosureId = closurePayments.ToDictionary(
                payment => payment.NegativeClosureId);
            foreach (var correction in closureCorrections)
            {
                if (!paymentByClosureId.TryGetValue(
                        correction.OriginalClosureId,
                        out var originalPayment)
                    || originalPayment.PaymentContext != "negative_closure")
                {
                    return null;
                }

                if (correction.Mode == "cancel")
                {
                    if (correction.ReplacementClosureId is not null)
                    {
                        return null;
                    }

                    negativeCancellations[correction.Id] =
                        new CanonicalPaymentCancellationSourceRow(
                            correction.Id,
                            originalPayment.PaymentId,
                            correction.Reason,
                            correction.OccurredAt,
                            correction.RecordedAt,
                            correction.RecordedByAccountId,
                            correction.SessionId,
                            correction.EntryOrigin,
                            correction.EntryBatchId);
                    relevantPaymentIds.Add(originalPayment.PaymentId);
                    continue;
                }

                if (correction.Mode != "replace"
                    || correction.ReplacementClosureId is not { } replacementClosureId
                    || !paymentByClosureId.TryGetValue(
                        replacementClosureId,
                        out var replacementPayment)
                    || replacementPayment.PaymentContext != "negative_closure"
                    || replacementPayment.ClientId != originalPayment.ClientId)
                {
                    return null;
                }

                negativeCorrections[correction.Id] =
                    new CanonicalPaymentCorrectionSourceRow(
                        correction.Id,
                        originalPayment.ClientId,
                        originalPayment.PaymentId,
                        replacementPayment.PaymentId,
                        NegativeCoverageChangedFieldsJson,
                        correction.Reason,
                        correction.OccurredAt,
                        correction.RecordedAt,
                        correction.RecordedByAccountId,
                        correction.SessionId,
                        correction.EntryOrigin,
                        correction.EntryBatchId);
                relevantPaymentIds.Add(originalPayment.PaymentId);
                relevantPaymentIds.Add(replacementPayment.PaymentId);
            }
        }

        var allPaymentIds = relevantPaymentIds.ToArray();
        var genericCancellations = await dbContext.Set<PaymentCancellationRecord>()
            .AsNoTracking()
            .Where(cancellation => allPaymentIds.Contains(cancellation.PaymentId))
            .Select(cancellation => new CanonicalPaymentCancellationSourceRow(
                cancellation.Id,
                cancellation.PaymentId,
                cancellation.Reason,
                cancellation.OccurredAt,
                cancellation.RecordedAt,
                cancellation.RecordedByAccountId,
                cancellation.SessionId,
                cancellation.EntryOrigin,
                cancellation.EntryBatchId))
            .ToArrayAsync(cancellationToken);
        var cancellationRows = genericCancellations
            .Concat(saleCancellations.Values)
            .Concat(negativeCancellations.Values)
            .ToArray();
        var correctionRows = genericCorrections.Values
            .Concat(saleCorrections.Values)
            .Concat(negativeCorrections.Values)
            .ToArray();

        if (cancellationRows
                .GroupBy(row => row.PaymentId)
                .Any(group => group.Count() > 1)
            || correctionRows
                .GroupBy(row => row.OriginalPaymentId)
                .Any(group => group.Count() > 1)
            || correctionRows
                .GroupBy(row => row.ReplacementPaymentId)
                .Any(group => group.Count() > 1))
        {
            return null;
        }

        return new CanonicalPaymentRelations(
            relevantPaymentIds.ToArray(),
            cancellationRows.ToDictionary(row => row.PaymentId),
            correctionRows.ToDictionary(row => row.ReplacementPaymentId),
            correctionRows.ToDictionary(row => row.OriginalPaymentId));
    }

    internal static QueryPermissionSet BuildCorrectionPermissions(
        ActorContext actor,
        ClientPaymentRowStatus paymentStatus,
        PaymentContext paymentContext,
        PaymentDayReconciliationStatus dayStatus)
    {
        if (paymentStatus != ClientPaymentRowStatus.Active
            || paymentContext is PaymentContext.MembershipSale or PaymentContext.NegativeClosure)
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
            || cancellationSource.CancellationId == Guid.Empty
            || cancellationSource.RecordedByAccountId == Guid.Empty
            || cancellationSource.SessionId == Guid.Empty
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
            || correctionSource.CorrectionId == Guid.Empty
            || correctionSource.ClientId != source.ClientId
            || correctionSource.OriginalPaymentId == Guid.Empty
            || correctionSource.ReplacementPaymentId == Guid.Empty
            || correctionSource.OriginalPaymentId
                == correctionSource.ReplacementPaymentId
            || correctionSource.RecordedByAccountId == Guid.Empty
            || correctionSource.SessionId == Guid.Empty
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

    internal sealed record CanonicalPaymentRelations(
        IReadOnlyCollection<Guid> PaymentIds,
        IReadOnlyDictionary<Guid, CanonicalPaymentCancellationSourceRow>
            CancellationsByPaymentId,
        IReadOnlyDictionary<Guid, CanonicalPaymentCorrectionSourceRow>
            CorrectionsFromOriginalByPaymentId,
        IReadOnlyDictionary<Guid, CanonicalPaymentCorrectionSourceRow>
            CorrectionsToReplacementByPaymentId)
    {
        internal static CanonicalPaymentRelations Empty { get; } = new(
            [],
            new Dictionary<Guid, CanonicalPaymentCancellationSourceRow>(),
            new Dictionary<Guid, CanonicalPaymentCorrectionSourceRow>(),
            new Dictionary<Guid, CanonicalPaymentCorrectionSourceRow>());
    }

    private sealed record NegativeClosurePaymentRelationRow(
        Guid PaymentId,
        Guid ClientId,
        Guid NegativeClosureId,
        string PaymentContext);
}
