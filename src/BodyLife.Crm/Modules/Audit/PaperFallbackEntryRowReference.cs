namespace BodyLife.Crm.Modules.Audit;

/// <summary>
/// Immutable provenance resolved from a numbered paper-fallback sheet row.
/// Domain commands use this reference instead of accepting paper metadata as
/// caller-controlled free text.
/// </summary>
public sealed record PaperFallbackEntryRowReference(
    Guid EntryBatchId,
    Guid EntryBatchRowId,
    string PaperSheetNumber,
    int LineNumber,
    PaperFallbackEventType EventType,
    DateTimeOffset OccurredAt,
    string Explanation);
