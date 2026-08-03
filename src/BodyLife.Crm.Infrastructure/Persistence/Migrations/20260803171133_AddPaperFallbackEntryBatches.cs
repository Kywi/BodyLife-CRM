using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BodyLife.Crm.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPaperFallbackEntryBatches : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "entry_batches",
                schema: "bodylife",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    batch_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    paper_sheet_number = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    business_date_start = table.Column<DateOnly>(type: "date", nullable: false),
                    business_date_end = table.Column<DateOnly>(type: "date", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recorded_by_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reconciled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    reconciled_by_account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    note = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_entry_batches", x => x.id);
                    table.CheckConstraint("ck_entry_batches_business_date_range", "business_date_start <= business_date_end");
                    table.CheckConstraint("ck_entry_batches_paper_sheet_number", "length(btrim(paper_sheet_number)) > 0 and paper_sheet_number = upper(btrim(paper_sheet_number))");
                    table.CheckConstraint("ck_entry_batches_reconciliation", "(reconciled_at is null) = (reconciled_by_account_id is null) and (reconciled_at is null or reconciled_at >= recorded_at)");
                    table.CheckConstraint("ck_entry_batches_type", "batch_type in ('manual_backfill', 'paper_fallback')");
                    table.ForeignKey(
                        name: "FK_entry_batches_accounts_reconciled_by_account_id",
                        column: x => x.reconciled_by_account_id,
                        principalSchema: "bodylife",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_entry_batches_accounts_recorded_by_account_id",
                        column: x => x.recorded_by_account_id,
                        principalSchema: "bodylife",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "entry_batch_rows",
                schema: "bodylife",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    entry_batch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    line_number = table.Column<int>(type: "integer", nullable: false),
                    event_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    explanation = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recorded_by_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_entry_batch_rows", x => x.id);
                    table.CheckConstraint("ck_entry_batch_rows_event_type", "event_type in ('visit', 'payment', 'freeze', 'membership_sale', 'negative_coverage', 'correction_or_cancellation')");
                    table.CheckConstraint("ck_entry_batch_rows_explanation", "length(btrim(explanation)) > 0");
                    table.CheckConstraint("ck_entry_batch_rows_line_number", "line_number > 0");
                    table.ForeignKey(
                        name: "FK_entry_batch_rows_accounts_recorded_by_account_id",
                        column: x => x.recorded_by_account_id,
                        principalSchema: "bodylife",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_entry_batch_rows_entry_batches_entry_batch_id",
                        column: x => x.entry_batch_id,
                        principalSchema: "bodylife",
                        principalTable: "entry_batches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_entry_batch_rows_sessions_session_id",
                        column: x => x.session_id,
                        principalSchema: "bodylife",
                        principalTable: "sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "entry_batch_row_entities",
                schema: "bodylife",
                columns: table => new
                {
                    entry_batch_row_id = table.Column<Guid>(type: "uuid", nullable: false),
                    entity_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    entity_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_entry_batch_row_entities", x => new { x.entry_batch_row_id, x.entity_type, x.entity_id });
                    table.CheckConstraint("ck_entry_batch_row_entities_entity_id", "entity_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("ck_entry_batch_row_entities_entity_type", "length(btrim(entity_type)) > 0");
                    table.ForeignKey(
                        name: "FK_entry_batch_row_entities_entry_batch_rows_entry_batch_row_id",
                        column: x => x.entry_batch_row_id,
                        principalSchema: "bodylife",
                        principalTable: "entry_batch_rows",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ux_entry_batch_row_entities_entity",
                schema: "bodylife",
                table: "entry_batch_row_entities",
                columns: new[] { "entity_type", "entity_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_entry_batch_rows_batch_occurred_at",
                schema: "bodylife",
                table: "entry_batch_rows",
                columns: new[] { "entry_batch_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "IX_entry_batch_rows_recorded_by_account_id",
                schema: "bodylife",
                table: "entry_batch_rows",
                column: "recorded_by_account_id");

            migrationBuilder.CreateIndex(
                name: "IX_entry_batch_rows_session_id",
                schema: "bodylife",
                table: "entry_batch_rows",
                column: "session_id");

            migrationBuilder.CreateIndex(
                name: "ux_entry_batch_rows_batch_line_number",
                schema: "bodylife",
                table: "entry_batch_rows",
                columns: new[] { "entry_batch_id", "line_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_entry_batches_business_range_recorded_at",
                schema: "bodylife",
                table: "entry_batches",
                columns: new[] { "business_date_start", "business_date_end", "recorded_at" });

            migrationBuilder.CreateIndex(
                name: "IX_entry_batches_reconciled_by_account_id",
                schema: "bodylife",
                table: "entry_batches",
                column: "reconciled_by_account_id");

            migrationBuilder.CreateIndex(
                name: "IX_entry_batches_recorded_by_account_id",
                schema: "bodylife",
                table: "entry_batches",
                column: "recorded_by_account_id");

            migrationBuilder.CreateIndex(
                name: "ux_entry_batches_paper_sheet_number",
                schema: "bodylife",
                table: "entry_batches",
                column: "paper_sheet_number",
                unique: true,
                filter: "batch_type = 'paper_fallback'");

            migrationBuilder.Sql(
                """
                create function bodylife.enforce_entry_batch_row_parent()
                returns trigger
                language plpgsql
                as $function$
                declare
                    parent_type text;
                    parent_start date;
                    parent_end date;
                    row_business_date date;
                begin
                    select batch_type, business_date_start, business_date_end
                    into parent_type, parent_start, parent_end
                    from bodylife.entry_batches
                    where id = new.entry_batch_id;

                    row_business_date :=
                        (new.occurred_at at time zone 'Europe/Kyiv')::date;

                    if parent_type is null
                        or parent_type not in ('manual_backfill', 'paper_fallback')
                        or row_business_date < parent_start
                        or row_business_date > parent_end then
                        raise exception
                            using errcode = '23514',
                                  constraint = 'ck_entry_batch_rows_parent',
                                  message = 'entry batch row does not match its parent';
                    end if;

                    return new;
                end;
                $function$;

                create constraint trigger ck_entry_batch_rows_parent
                after insert or update of entry_batch_id, occurred_at
                on bodylife.entry_batch_rows
                deferrable initially immediate
                for each row
                execute function bodylife.enforce_entry_batch_row_parent();

                create function bodylife.enforce_entry_batch_immutable_fields()
                returns trigger
                language plpgsql
                as $function$
                begin
                    if new.batch_type is distinct from old.batch_type
                        or new.paper_sheet_number is distinct from old.paper_sheet_number
                        or new.business_date_start is distinct from old.business_date_start
                        or new.business_date_end is distinct from old.business_date_end
                        or new.recorded_at is distinct from old.recorded_at
                        or new.recorded_by_account_id is distinct from old.recorded_by_account_id
                        or new.note is distinct from old.note then
                        raise exception
                            using errcode = '23514',
                                  constraint = 'ck_entry_batches_immutable',
                                  message = 'entry batch identity and source context are immutable';
                    end if;

                    return new;
                end;
                $function$;

                create trigger ck_entry_batches_immutable
                before update of
                    batch_type,
                    paper_sheet_number,
                    business_date_start,
                    business_date_end,
                    recorded_at,
                    recorded_by_account_id,
                    note
                on bodylife.entry_batches
                for each row
                execute function bodylife.enforce_entry_batch_immutable_fields();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "entry_batch_row_entities",
                schema: "bodylife");

            migrationBuilder.DropTable(
                name: "entry_batch_rows",
                schema: "bodylife");

            migrationBuilder.DropTable(
                name: "entry_batches",
                schema: "bodylife");

            migrationBuilder.Sql(
                """
                drop function bodylife.enforce_entry_batch_row_parent();
                drop function bodylife.enforce_entry_batch_immutable_fields();
                """);
        }
    }
}
