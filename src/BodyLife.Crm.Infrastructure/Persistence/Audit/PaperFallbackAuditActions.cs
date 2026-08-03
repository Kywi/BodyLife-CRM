namespace BodyLife.Crm.Infrastructure.Persistence.Audit;

public static class PaperFallbackAuditActions
{
    public const string BatchEntityType = "entry_batch";
    public const string RowEntityType = "entry_batch_row";
    public const string BatchCreated = "paper_fallback.batch_created";
    public const string RowCreated = "paper_fallback.row_created";
}
