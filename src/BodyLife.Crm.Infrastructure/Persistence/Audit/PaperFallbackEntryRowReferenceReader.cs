using System.Text.Json;
using BodyLife.Crm.Modules.Audit;
using BodyLife.Crm.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BodyLife.Crm.Infrastructure.Persistence.Audit;

internal sealed class PaperFallbackEntryRowReferenceReader(
    BodyLifeDbContext dbContext)
{
    internal async Task<Dictionary<Guid, PaperFallbackEntryRowReference>?> LoadAsync(
        IReadOnlyList<PaperFallbackEntryRowReferenceSource> sources,
        string entityType,
        PaperFallbackEventType expectedEventType,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sources);
        if (sources.Count == 0)
        {
            return new Dictionary<Guid, PaperFallbackEntryRowReference>();
        }

        if (sources.Any(source => source.EntityId == Guid.Empty)
            || sources.Select(source => source.EntityId).Distinct().Count()
                != sources.Count)
        {
            return null;
        }

        var paperSources = sources
            .Where(source => source.EntryOrigin == "paper_fallback")
            .ToArray();
        var sourceIds = sources.Select(source => source.EntityId).ToArray();
        var links = await dbContext.Set<EntryBatchRowEntityRecord>()
            .AsNoTracking()
            .Where(link =>
                link.EntityType == entityType
                && sourceIds.Contains(link.EntityId))
            .ToArrayAsync(cancellationToken);
        if (links.GroupBy(link => link.EntityId).Any(group => group.Count() != 1)
            || !links.Select(link => link.EntityId).Order()
                .SequenceEqual(paperSources.Select(source => source.EntityId).Order()))
        {
            return null;
        }

        if (links.Length == 0)
        {
            return new Dictionary<Guid, PaperFallbackEntryRowReference>();
        }

        var rowIds = links.Select(link => link.EntryBatchRowId).ToArray();
        var rows = await dbContext.Set<EntryBatchRowRecord>()
            .AsNoTracking()
            .Where(row => rowIds.Contains(row.Id))
            .ToArrayAsync(cancellationToken);
        var batchIds = rows.Select(row => row.EntryBatchId).Distinct().ToArray();
        var batches = await dbContext.Set<EntryBatchRecord>()
            .AsNoTracking()
            .Where(batch => batchIds.Contains(batch.Id))
            .ToArrayAsync(cancellationToken);
        if (rows.Length != links.Length || batches.Length != batchIds.Length)
        {
            return null;
        }

        var batchesById = batches.ToDictionary(batch => batch.Id);
        var rowsById = rows.ToDictionary(row => row.Id);
        var sourcesById = paperSources.ToDictionary(source => source.EntityId);
        var result = new Dictionary<Guid, PaperFallbackEntryRowReference>();
        foreach (var link in links)
        {
            var row = rowsById[link.EntryBatchRowId];
            var batch = batchesById[row.EntryBatchId];
            var source = sourcesById[link.EntityId];
            var sourceExpectedEventType = source.ExpectedEventType ?? expectedEventType;
            if (batch.BatchType != "paper_fallback"
                || row.EventType
                    != PaperFallbackCommandSupport.MapEventType(sourceExpectedEventType)
                || string.IsNullOrWhiteSpace(batch.PaperSheetNumber)
                || batch.PaperSheetNumber
                    != batch.PaperSheetNumber.Trim().ToUpperInvariant()
                || row.LineNumber <= 0
                || string.IsNullOrWhiteSpace(row.Explanation)
                || row.Explanation != row.Explanation.Trim()
                || row.RecordedByAccountId != source.RecordedByAccountId
                || row.SessionId != source.SessionId
                || source.EntryBatchId != batch.Id
                || BusinessTimeZone.GetBusinessDate(row.OccurredAt)
                    < batch.BusinessDateStart
                || BusinessTimeZone.GetBusinessDate(row.OccurredAt)
                    > batch.BusinessDateEnd
                || !SamePostgreSqlInstant(row.OccurredAt, source.OccurredAt))
            {
                return null;
            }

            result.Add(
                source.EntityId,
                new PaperFallbackEntryRowReference(
                    batch.Id,
                    row.Id,
                    batch.PaperSheetNumber,
                    row.LineNumber,
                    sourceExpectedEventType,
                    row.OccurredAt,
                    row.Explanation));
        }

        return result;
    }

    internal static bool HasMatchingAuditReference(
        ClientAuditEntry audit,
        Guid? sourceEntryBatchId,
        PaperFallbackEntryRowReference? paper)
    {
        try
        {
            using var document = JsonDocument.Parse(audit.RelatedEntityRefsJson);
            var root = document.RootElement;
            if (paper is null)
            {
                return IsMissingOrNull(root, "entryBatchId")
                    && IsMissingOrNull(root, "entryBatchRowId")
                    && IsMissingOrNull(root, "paperSheetNumber")
                    && IsMissingOrNull(root, "lineNumber")
                    && IsMissingOrNull(root, "paperExplanation");
            }

            return root.GetProperty("entryBatchId").GetGuid() == paper.EntryBatchId
                && root.GetProperty("entryBatchRowId").GetGuid()
                    == paper.EntryBatchRowId
                && root.GetProperty("paperSheetNumber").GetString()
                    == paper.PaperSheetNumber
                && root.GetProperty("lineNumber").GetInt32() == paper.LineNumber
                && root.GetProperty("paperExplanation").GetString()
                    == paper.Explanation
                && sourceEntryBatchId == paper.EntryBatchId;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (KeyNotFoundException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static bool IsMissingOrNull(JsonElement root, string propertyName) =>
        !root.TryGetProperty(propertyName, out var value)
        || value.ValueKind == JsonValueKind.Null;

    private static bool SamePostgreSqlInstant(
        DateTimeOffset left,
        DateTimeOffset right) =>
        left.UtcDateTime.Ticks / 10 == right.UtcDateTime.Ticks / 10;
}

internal sealed record PaperFallbackEntryRowReferenceSource(
    Guid EntityId,
    string EntryOrigin,
    Guid? EntryBatchId,
    DateTimeOffset OccurredAt,
    Guid RecordedByAccountId,
    Guid SessionId,
    PaperFallbackEventType? ExpectedEventType = null);
