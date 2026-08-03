using BodyLife.Crm.Infrastructure.Persistence.UsersRoles;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BodyLife.Crm.Infrastructure.Persistence.Audit;

internal sealed class EntryBatchRecordConfiguration
    : IEntityTypeConfiguration<EntryBatchRecord>
{
    public void Configure(EntityTypeBuilder<EntryBatchRecord> builder)
    {
        builder.ToTable(
            "entry_batches",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_entry_batches_type",
                    "batch_type in ('manual_backfill', 'paper_fallback')");
                table.HasCheckConstraint(
                    "ck_entry_batches_paper_sheet_number",
                    "length(btrim(paper_sheet_number)) > 0 "
                    + "and paper_sheet_number = upper(btrim(paper_sheet_number))");
                table.HasCheckConstraint(
                    "ck_entry_batches_business_date_range",
                    "business_date_start <= business_date_end");
                table.HasCheckConstraint(
                    "ck_entry_batches_reconciliation",
                    "(reconciled_at is null) = (reconciled_by_account_id is null) "
                    + "and (reconciled_at is null or reconciled_at >= recorded_at)");
            });

        builder.HasKey(batch => batch.Id);

        builder.Property(batch => batch.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();
        builder.Property(batch => batch.BatchType)
            .HasColumnName("batch_type")
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(batch => batch.PaperSheetNumber)
            .HasColumnName("paper_sheet_number")
            .HasMaxLength(128)
            .IsRequired();
        builder.Property(batch => batch.BusinessDateStart)
            .HasColumnName("business_date_start")
            .HasColumnType("date");
        builder.Property(batch => batch.BusinessDateEnd)
            .HasColumnName("business_date_end")
            .HasColumnType("date");
        builder.Property(batch => batch.RecordedAt)
            .HasColumnName("recorded_at");
        builder.Property(batch => batch.RecordedByAccountId)
            .HasColumnName("recorded_by_account_id");
        builder.Property(batch => batch.ReconciledAt)
            .HasColumnName("reconciled_at");
        builder.Property(batch => batch.ReconciledByAccountId)
            .HasColumnName("reconciled_by_account_id");
        builder.Property(batch => batch.Note)
            .HasColumnName("note")
            .HasMaxLength(2000);

        builder.HasOne<AccountRecord>()
            .WithMany()
            .HasForeignKey(batch => batch.RecordedByAccountId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AccountRecord>()
            .WithMany()
            .HasForeignKey(batch => batch.ReconciledByAccountId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(batch => batch.PaperSheetNumber)
            .IsUnique()
            .HasFilter("batch_type = 'paper_fallback'")
            .HasDatabaseName("ux_entry_batches_paper_sheet_number");
        builder.HasIndex(batch => new
        {
            batch.BusinessDateStart,
            batch.BusinessDateEnd,
            batch.RecordedAt,
        })
            .HasDatabaseName("ix_entry_batches_business_range_recorded_at");
    }
}

internal sealed class EntryBatchRowRecordConfiguration
    : IEntityTypeConfiguration<EntryBatchRowRecord>
{
    public void Configure(EntityTypeBuilder<EntryBatchRowRecord> builder)
    {
        builder.ToTable(
            "entry_batch_rows",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_entry_batch_rows_line_number",
                    "line_number > 0");
                table.HasCheckConstraint(
                    "ck_entry_batch_rows_event_type",
                    "event_type in ('visit', 'payment', 'freeze', "
                    + "'membership_sale', 'negative_coverage', "
                    + "'correction_or_cancellation')");
                table.HasCheckConstraint(
                    "ck_entry_batch_rows_explanation",
                    "length(btrim(explanation)) > 0");
            });

        builder.HasKey(row => row.Id);

        builder.Property(row => row.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();
        builder.Property(row => row.EntryBatchId)
            .HasColumnName("entry_batch_id");
        builder.Property(row => row.LineNumber)
            .HasColumnName("line_number");
        builder.Property(row => row.EventType)
            .HasColumnName("event_type")
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(row => row.OccurredAt)
            .HasColumnName("occurred_at");
        builder.Property(row => row.Explanation)
            .HasColumnName("explanation")
            .HasMaxLength(2000)
            .IsRequired();
        builder.Property(row => row.RecordedAt)
            .HasColumnName("recorded_at");
        builder.Property(row => row.RecordedByAccountId)
            .HasColumnName("recorded_by_account_id");
        builder.Property(row => row.SessionId)
            .HasColumnName("session_id");

        builder.HasOne<EntryBatchRecord>()
            .WithMany()
            .HasForeignKey(row => row.EntryBatchId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AccountRecord>()
            .WithMany()
            .HasForeignKey(row => row.RecordedByAccountId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SessionRecord>()
            .WithMany()
            .HasForeignKey(row => row.SessionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(row => new { row.EntryBatchId, row.LineNumber })
            .IsUnique()
            .HasDatabaseName("ux_entry_batch_rows_batch_line_number");
        builder.HasIndex(row => new { row.EntryBatchId, row.OccurredAt })
            .HasDatabaseName("ix_entry_batch_rows_batch_occurred_at");
    }
}

internal sealed class EntryBatchRowEntityRecordConfiguration
    : IEntityTypeConfiguration<EntryBatchRowEntityRecord>
{
    public void Configure(EntityTypeBuilder<EntryBatchRowEntityRecord> builder)
    {
        builder.ToTable(
            "entry_batch_row_entities",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_entry_batch_row_entities_entity_type",
                    "length(btrim(entity_type)) > 0");
                table.HasCheckConstraint(
                    "ck_entry_batch_row_entities_entity_id",
                    "entity_id <> '00000000-0000-0000-0000-000000000000'::uuid");
            });

        builder.HasKey(link => new
        {
            link.EntryBatchRowId,
            link.EntityType,
            link.EntityId,
        });

        builder.Property(link => link.EntryBatchRowId)
            .HasColumnName("entry_batch_row_id");
        builder.Property(link => link.EntityType)
            .HasColumnName("entity_type")
            .HasMaxLength(64)
            .IsRequired();
        builder.Property(link => link.EntityId)
            .HasColumnName("entity_id");

        builder.HasOne<EntryBatchRowRecord>()
            .WithMany()
            .HasForeignKey(link => link.EntryBatchRowId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(link => new { link.EntityType, link.EntityId })
            .IsUnique()
            .HasDatabaseName("ux_entry_batch_row_entities_entity");
    }
}
