using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BodyLife.Crm.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddNegativeCoverageFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_visit_consumptions_consumption_type",
                schema: "bodylife",
                table: "visit_consumptions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_visit_consumptions_source_fact_identity",
                schema: "bodylife",
                table: "visit_consumptions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_visit_consumptions_source_fact_type",
                schema: "bodylife",
                table: "visit_consumptions");

            migrationBuilder.AddColumn<Guid>(
                name: "negative_closure_id",
                schema: "bodylife",
                table: "payments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_visits_id_client_id",
                schema: "bodylife",
                table: "visits",
                columns: new[] { "id", "client_id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_visit_consumptions_id_client_id",
                schema: "bodylife",
                table: "visit_consumptions",
                columns: new[] { "id", "client_id" });

            migrationBuilder.CreateTable(
                name: "membership_negative_closures",
                schema: "bodylife",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    closure_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    covering_membership_id = table.Column<Guid>(type: "uuid", nullable: true),
                    oldest_open_negative_visit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    visits_count = table.Column<int>(type: "integer", nullable: false),
                    comment = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recorded_by_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    entry_origin = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    entry_batch_id = table.Column<Guid>(type: "uuid", nullable: true),
                    idempotency_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_membership_negative_closures", x => x.id);
                    table.UniqueConstraint("AK_membership_negative_closures_id_client_id", x => new { x.id, x.client_id });
                    table.CheckConstraint("ck_negative_closures_comment_not_empty", "comment is null or length(btrim(comment)) > 0");
                    table.CheckConstraint("ck_negative_closures_covering_shape", "(closure_type = 'one_off' and covering_membership_id is null) or (closure_type = 'new_membership' and covering_membership_id is not null)");
                    table.CheckConstraint("ck_negative_closures_origin", "entry_origin in ('normal', 'manual_backfill', 'paper_fallback', 'future_import')");
                    table.CheckConstraint("ck_negative_closures_status", "status in ('active', 'canceled', 'replaced')");
                    table.CheckConstraint("ck_negative_closures_type", "closure_type in ('one_off', 'new_membership')");
                    table.CheckConstraint("ck_negative_closures_visits_count", "visits_count > 0");
                    table.ForeignKey(
                        name: "FK_membership_negative_closures_accounts_recorded_by_account_id",
                        column: x => x.recorded_by_account_id,
                        principalSchema: "bodylife",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_membership_negative_closures_clients_client_id",
                        column: x => x.client_id,
                        principalSchema: "bodylife",
                        principalTable: "clients",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_membership_negative_closures_issued_memberships_covering_me~",
                        columns: x => new { x.covering_membership_id, x.client_id },
                        principalSchema: "bodylife",
                        principalTable: "issued_memberships",
                        principalColumns: new[] { "id", "client_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_membership_negative_closures_sessions_session_id",
                        column: x => x.session_id,
                        principalSchema: "bodylife",
                        principalTable: "sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_negative_closures_oldest_visit_client",
                        columns: x => new { x.oldest_open_negative_visit_id, x.client_id },
                        principalSchema: "bodylife",
                        principalTable: "visits",
                        principalColumns: new[] { "id", "client_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "membership_negative_closure_corrections",
                schema: "bodylife",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    original_closure_id = table.Column<Guid>(type: "uuid", nullable: false),
                    replacement_closure_id = table.Column<Guid>(type: "uuid", nullable: true),
                    mode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recorded_by_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    entry_origin = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    entry_batch_id = table.Column<Guid>(type: "uuid", nullable: true),
                    idempotency_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_membership_negative_closure_corrections", x => x.id);
                    table.CheckConstraint("ck_negative_closure_corrections_mode", "mode in ('cancel', 'replace')");
                    table.CheckConstraint("ck_negative_closure_corrections_origin", "entry_origin in ('normal', 'manual_backfill', 'paper_fallback', 'future_import')");
                    table.CheckConstraint("ck_negative_closure_corrections_reason", "length(btrim(reason)) > 0");
                    table.CheckConstraint("ck_negative_closure_corrections_shape", "(mode = 'cancel' and replacement_closure_id is null) or (mode = 'replace' and replacement_closure_id is not null and replacement_closure_id <> original_closure_id)");
                    table.ForeignKey(
                        name: "FK_membership_negative_closure_corrections_accounts_recorded_b~",
                        column: x => x.recorded_by_account_id,
                        principalSchema: "bodylife",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_membership_negative_closure_corrections_membership_negative~",
                        column: x => x.original_closure_id,
                        principalSchema: "bodylife",
                        principalTable: "membership_negative_closures",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_membership_negative_closure_corrections_membership_negativ~1",
                        column: x => x.replacement_closure_id,
                        principalSchema: "bodylife",
                        principalTable: "membership_negative_closures",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_membership_negative_closure_corrections_sessions_session_id",
                        column: x => x.session_id,
                        principalSchema: "bodylife",
                        principalTable: "sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "membership_negative_closure_lines",
                schema: "bodylife",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    negative_closure_id = table.Column<Guid>(type: "uuid", nullable: false),
                    membership_type_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type_name_snapshot = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    duration_days_snapshot = table.Column<int>(type: "integer", nullable: false),
                    visits_limit_snapshot = table.Column<int>(type: "integer", nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    unit_price_amount_snapshot = table.Column<decimal>(type: "numeric", nullable: false),
                    currency_snapshot = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    line_total = table.Column<decimal>(type: "numeric", nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_membership_negative_closure_lines", x => x.id);
                    table.UniqueConstraint("AK_negative_closure_lines_id_closure_id", x => new { x.id, x.negative_closure_id });
                    table.CheckConstraint("ck_negative_closure_lines_quantity", "quantity > 0");
                    table.CheckConstraint("ck_negative_closure_lines_sequence", "sequence > 0");
                    table.CheckConstraint("ck_negative_closure_lines_snapshots", "length(btrim(type_name_snapshot)) > 0 and duration_days_snapshot > 0 and visits_limit_snapshot = 1 and unit_price_amount_snapshot > 0 and line_total = quantity * unit_price_amount_snapshot and length(btrim(currency_snapshot)) > 0 and currency_snapshot = upper(btrim(currency_snapshot))");
                    table.ForeignKey(
                        name: "FK_membership_negative_closure_lines_membership_negative_closu~",
                        column: x => x.negative_closure_id,
                        principalSchema: "bodylife",
                        principalTable: "membership_negative_closures",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_membership_negative_closure_lines_membership_types_membersh~",
                        column: x => x.membership_type_id,
                        principalSchema: "bodylife",
                        principalTable: "membership_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "membership_negative_closure_items",
                schema: "bodylife",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    negative_closure_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    closure_line_id = table.Column<Guid>(type: "uuid", nullable: true),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    visit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_membership_id = table.Column<Guid>(type: "uuid", nullable: false),
                    old_consumption_id = table.Column<Guid>(type: "uuid", nullable: false),
                    covering_membership_id = table.Column<Guid>(type: "uuid", nullable: true),
                    new_consumption_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_membership_negative_closure_items", x => x.id);
                    table.CheckConstraint("ck_negative_closure_items_sequence", "sequence > 0");
                    table.CheckConstraint("ck_negative_closure_items_status", "status in ('active', 'canceled', 'replaced')");
                    table.ForeignKey(
                        name: "FK_membership_negative_closure_items_issued_memberships_coveri~",
                        columns: x => new { x.covering_membership_id, x.client_id },
                        principalSchema: "bodylife",
                        principalTable: "issued_memberships",
                        principalColumns: new[] { "id", "client_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_membership_negative_closure_items_issued_memberships_source~",
                        columns: x => new { x.source_membership_id, x.client_id },
                        principalSchema: "bodylife",
                        principalTable: "issued_memberships",
                        principalColumns: new[] { "id", "client_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_membership_negative_closure_items_membership_negative_closu~",
                        columns: x => new { x.negative_closure_id, x.client_id },
                        principalSchema: "bodylife",
                        principalTable: "membership_negative_closures",
                        principalColumns: new[] { "id", "client_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_membership_negative_closure_items_visit_consumptions_new_co~",
                        columns: x => new { x.new_consumption_id, x.client_id },
                        principalSchema: "bodylife",
                        principalTable: "visit_consumptions",
                        principalColumns: new[] { "id", "client_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_membership_negative_closure_items_visit_consumptions_old_co~",
                        columns: x => new { x.old_consumption_id, x.client_id },
                        principalSchema: "bodylife",
                        principalTable: "visit_consumptions",
                        principalColumns: new[] { "id", "client_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_membership_negative_closure_items_visits_visit_id_client_id",
                        columns: x => new { x.visit_id, x.client_id },
                        principalSchema: "bodylife",
                        principalTable: "visits",
                        principalColumns: new[] { "id", "client_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_negative_closure_items_line_closure",
                        columns: x => new { x.closure_line_id, x.negative_closure_id },
                        principalSchema: "bodylife",
                        principalTable: "membership_negative_closure_lines",
                        principalColumns: new[] { "id", "negative_closure_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ux_visit_consumptions_active_negative_coverage_source",
                schema: "bodylife",
                table: "visit_consumptions",
                column: "source_fact_id",
                unique: true,
                filter: "status = 'active' and consumption_type = 'negative_coverage'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_visit_consumptions_consumption_type",
                schema: "bodylife",
                table: "visit_consumptions",
                sql: "consumption_type in ('counted', 'negative_coverage')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_visit_consumptions_source_fact_identity",
                schema: "bodylife",
                table: "visit_consumptions",
                sql: "consumption_type <> 'counted' or source_fact_id = visit_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_visit_consumptions_source_fact_type",
                schema: "bodylife",
                table: "visit_consumptions",
                sql: "(consumption_type = 'counted' and source_fact_type = 'visit') or (consumption_type = 'negative_coverage' and source_fact_type = 'negative_closure_item')");

            migrationBuilder.CreateIndex(
                name: "IX_payments_negative_closure_id_client_id",
                schema: "bodylife",
                table: "payments",
                columns: new[] { "negative_closure_id", "client_id" });

            migrationBuilder.CreateIndex(
                name: "ux_payments_active_negative_closure",
                schema: "bodylife",
                table: "payments",
                column: "negative_closure_id",
                unique: true,
                filter: "payment_context = 'negative_closure' and status = 'active'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_payments_negative_closure_context",
                schema: "bodylife",
                table: "payments",
                sql: "(payment_context = 'negative_closure' and negative_closure_id is not null and membership_id is null) or (payment_context <> 'negative_closure' and negative_closure_id is null)");

            migrationBuilder.CreateIndex(
                name: "IX_membership_negative_closure_corrections_recorded_by_account~",
                schema: "bodylife",
                table: "membership_negative_closure_corrections",
                column: "recorded_by_account_id");

            migrationBuilder.CreateIndex(
                name: "IX_membership_negative_closure_corrections_replacement_closure~",
                schema: "bodylife",
                table: "membership_negative_closure_corrections",
                column: "replacement_closure_id");

            migrationBuilder.CreateIndex(
                name: "IX_membership_negative_closure_corrections_session_id",
                schema: "bodylife",
                table: "membership_negative_closure_corrections",
                column: "session_id");

            migrationBuilder.CreateIndex(
                name: "ux_negative_closure_corrections_idempotency_key",
                schema: "bodylife",
                table: "membership_negative_closure_corrections",
                column: "idempotency_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_negative_closure_corrections_original",
                schema: "bodylife",
                table: "membership_negative_closure_corrections",
                column: "original_closure_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_negative_closure_corrections_replacement",
                schema: "bodylife",
                table: "membership_negative_closure_corrections",
                column: "replacement_closure_id",
                unique: true,
                filter: "replacement_closure_id is not null");

            migrationBuilder.CreateIndex(
                name: "IX_membership_negative_closure_items_closure_line_id_negative_~",
                schema: "bodylife",
                table: "membership_negative_closure_items",
                columns: new[] { "closure_line_id", "negative_closure_id" });

            migrationBuilder.CreateIndex(
                name: "IX_membership_negative_closure_items_covering_membership_id_cl~",
                schema: "bodylife",
                table: "membership_negative_closure_items",
                columns: new[] { "covering_membership_id", "client_id" });

            migrationBuilder.CreateIndex(
                name: "IX_membership_negative_closure_items_negative_closure_id_clien~",
                schema: "bodylife",
                table: "membership_negative_closure_items",
                columns: new[] { "negative_closure_id", "client_id" });

            migrationBuilder.CreateIndex(
                name: "IX_membership_negative_closure_items_new_consumption_id_client~",
                schema: "bodylife",
                table: "membership_negative_closure_items",
                columns: new[] { "new_consumption_id", "client_id" });

            migrationBuilder.CreateIndex(
                name: "IX_membership_negative_closure_items_old_consumption_id_client~",
                schema: "bodylife",
                table: "membership_negative_closure_items",
                columns: new[] { "old_consumption_id", "client_id" });

            migrationBuilder.CreateIndex(
                name: "IX_membership_negative_closure_items_source_membership_id_clie~",
                schema: "bodylife",
                table: "membership_negative_closure_items",
                columns: new[] { "source_membership_id", "client_id" });

            migrationBuilder.CreateIndex(
                name: "IX_membership_negative_closure_items_visit_id_client_id",
                schema: "bodylife",
                table: "membership_negative_closure_items",
                columns: new[] { "visit_id", "client_id" });

            migrationBuilder.CreateIndex(
                name: "ix_negative_closure_items_covering_membership",
                schema: "bodylife",
                table: "membership_negative_closure_items",
                column: "covering_membership_id");

            migrationBuilder.CreateIndex(
                name: "ix_negative_closure_items_source_membership",
                schema: "bodylife",
                table: "membership_negative_closure_items",
                column: "source_membership_id");

            migrationBuilder.CreateIndex(
                name: "ix_negative_closure_items_visit",
                schema: "bodylife",
                table: "membership_negative_closure_items",
                column: "visit_id",
                unique: true,
                filter: "status = 'active'");

            migrationBuilder.CreateIndex(
                name: "ux_negative_closure_items_new_consumption",
                schema: "bodylife",
                table: "membership_negative_closure_items",
                column: "new_consumption_id",
                unique: true,
                filter: "new_consumption_id is not null");

            migrationBuilder.CreateIndex(
                name: "ux_negative_closure_items_sequence",
                schema: "bodylife",
                table: "membership_negative_closure_items",
                columns: new[] { "negative_closure_id", "sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_membership_negative_closure_lines_membership_type_id",
                schema: "bodylife",
                table: "membership_negative_closure_lines",
                column: "membership_type_id");

            migrationBuilder.CreateIndex(
                name: "ux_negative_closure_lines_sequence",
                schema: "bodylife",
                table: "membership_negative_closure_lines",
                columns: new[] { "negative_closure_id", "sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_membership_negative_closures_covering_membership_id_client_~",
                schema: "bodylife",
                table: "membership_negative_closures",
                columns: new[] { "covering_membership_id", "client_id" });

            migrationBuilder.CreateIndex(
                name: "IX_membership_negative_closures_oldest_open_negative_visit_id_~",
                schema: "bodylife",
                table: "membership_negative_closures",
                columns: new[] { "oldest_open_negative_visit_id", "client_id" });

            migrationBuilder.CreateIndex(
                name: "IX_membership_negative_closures_recorded_by_account_id",
                schema: "bodylife",
                table: "membership_negative_closures",
                column: "recorded_by_account_id");

            migrationBuilder.CreateIndex(
                name: "IX_membership_negative_closures_session_id",
                schema: "bodylife",
                table: "membership_negative_closures",
                column: "session_id");

            migrationBuilder.CreateIndex(
                name: "ix_negative_closures_client_timeline",
                schema: "bodylife",
                table: "membership_negative_closures",
                columns: new[] { "client_id", "recorded_at" });

            migrationBuilder.CreateIndex(
                name: "ux_negative_closures_idempotency_key",
                schema: "bodylife",
                table: "membership_negative_closures",
                column: "idempotency_key",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_payments_negative_closures_closure_client",
                schema: "bodylife",
                table: "payments",
                columns: new[] { "negative_closure_id", "client_id" },
                principalSchema: "bodylife",
                principalTable: "membership_negative_closures",
                principalColumns: new[] { "id", "client_id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql(
                """
                create function bodylife.assert_membership_negative_closure(
                    target_closure_id uuid)
                returns void
                language plpgsql
                as $function$
                declare
                    target_type text;
                    target_status text;
                    target_client_id uuid;
                    target_covering_membership_id uuid;
                    target_oldest_visit_id uuid;
                    target_visits_count integer;
                    target_occurred_at timestamptz;
                    target_recorded_at timestamptz;
                    target_actor_id uuid;
                    target_session_id uuid;
                    target_entry_origin text;
                    target_entry_batch_id uuid;
                    item_count bigint;
                    matching_item_status_count bigint;
                    malformed_item_count bigint;
                    minimum_item_sequence integer;
                    maximum_item_sequence integer;
                    first_item_visit_id uuid;
                    line_count bigint;
                    line_quantity_total bigint;
                    line_total numeric;
                    line_currency text;
                    line_currency_count bigint;
                    malformed_line_count bigint;
                    invalid_line_type_count bigint;
                    minimum_line_sequence integer;
                    maximum_line_sequence integer;
                    payment_count bigint;
                    matching_payment_count bigint;
                    correction_count bigint;
                    matching_correction_count bigint;
                    covering_visits_limit integer;
                    covering_start_date date;
                    covering_status text;
                    covering_mode text;
                    oldest_visit_date date;
                begin
                    if target_closure_id is null then
                        return;
                    end if;

                    select
                        closure_type,
                        status,
                        client_id,
                        covering_membership_id,
                        oldest_open_negative_visit_id,
                        visits_count,
                        occurred_at,
                        recorded_at,
                        recorded_by_account_id,
                        session_id,
                        entry_origin,
                        entry_batch_id
                    into
                        target_type,
                        target_status,
                        target_client_id,
                        target_covering_membership_id,
                        target_oldest_visit_id,
                        target_visits_count,
                        target_occurred_at,
                        target_recorded_at,
                        target_actor_id,
                        target_session_id,
                        target_entry_origin,
                        target_entry_batch_id
                    from bodylife.membership_negative_closures
                    where id = target_closure_id;

                    if not found then
                        return;
                    end if;

                    select
                        count(*),
                        count(*) filter (where item.status = target_status),
                        min(item.sequence),
                        max(item.sequence),
                        (min(item.visit_id::text) filter (
                            where item.sequence = 1))::uuid,
                        count(*) filter (
                            where visit.id is null
                               or source_membership.id is null
                               or old_consumption.id is null
                               or old_consumption.visit_id <> item.visit_id
                               or old_consumption.membership_id <> item.source_membership_id
                               or old_consumption.client_id <> item.client_id
                               or old_consumption.consumption_type <> 'counted'
                               or old_consumption.source_fact_type <> 'visit'
                               or old_consumption.source_fact_id <> item.visit_id
                               or (target_status = 'active' and (
                                    visit.status <> 'active'
                                    or source_membership.status <> 'active'
                                    or old_consumption.status <> 'active')))
                    into
                        item_count,
                        matching_item_status_count,
                        minimum_item_sequence,
                        maximum_item_sequence,
                        first_item_visit_id,
                        malformed_item_count
                    from bodylife.membership_negative_closure_items item
                    left join bodylife.visits visit
                        on visit.id = item.visit_id
                       and visit.client_id = item.client_id
                    left join bodylife.issued_memberships source_membership
                        on source_membership.id = item.source_membership_id
                       and source_membership.client_id = item.client_id
                    left join bodylife.visit_consumptions old_consumption
                        on old_consumption.id = item.old_consumption_id
                       and old_consumption.client_id = item.client_id
                    where item.negative_closure_id = target_closure_id;

                    if item_count <> target_visits_count
                        or matching_item_status_count <> item_count
                        or malformed_item_count <> 0
                        or minimum_item_sequence <> 1
                        or maximum_item_sequence <> target_visits_count
                        or first_item_visit_id is distinct from target_oldest_visit_id then
                        raise exception using
                            errcode = '23514',
                            constraint = 'ck_negative_closure_items_canonical_shape',
                            message = 'Negative closure items must retain a contiguous, lifecycle-consistent Visit allocation.';
                    end if;

                    select
                        count(*),
                        coalesce(sum(line.quantity), 0),
                        coalesce(sum(line.line_total), 0),
                        min(line.currency_snapshot),
                        count(distinct line.currency_snapshot),
                        min(line.sequence),
                        max(line.sequence),
                        count(*) filter (where (
                            select count(*)
                            from bodylife.membership_negative_closure_items item
                            where item.negative_closure_id = line.negative_closure_id
                              and item.closure_line_id = line.id) <> line.quantity)
                    into
                        line_count,
                        line_quantity_total,
                        line_total,
                        line_currency,
                        line_currency_count,
                        minimum_line_sequence,
                        maximum_line_sequence,
                        malformed_line_count
                    from bodylife.membership_negative_closure_lines line
                    where line.negative_closure_id = target_closure_id;

                    select count(*)
                    into payment_count
                    from bodylife.payments payment
                    where payment.negative_closure_id = target_closure_id
                      and payment.payment_context = 'negative_closure';

                    if target_type = 'one_off' then
                        select count(*)
                        into malformed_item_count
                        from bodylife.membership_negative_closure_items item
                        where item.negative_closure_id = target_closure_id
                          and (item.closure_line_id is null
                            or item.covering_membership_id is not null
                            or item.new_consumption_id is not null);

                        select count(*)
                        into invalid_line_type_count
                        from bodylife.membership_negative_closure_lines line
                        join bodylife.membership_types membership_type
                            on membership_type.id = line.membership_type_id
                        where line.negative_closure_id = target_closure_id
                          and membership_type.kind <> 'one_off';

                        if target_covering_membership_id is not null
                            or item_count = 0
                            or malformed_item_count <> 0
                            or line_count = 0
                            or line_quantity_total <> target_visits_count
                            or line_currency_count <> 1
                            or malformed_line_count <> 0
                            or invalid_line_type_count <> 0
                            or minimum_line_sequence <> 1
                            or maximum_line_sequence <> line_count then
                            raise exception using
                                errcode = '23514',
                                constraint = 'ck_negative_closure_one_off_shape',
                                message = 'One-off negative closure lines and items are inconsistent.';
                        end if;

                        select
                            count(*) filter (
                                where payment.amount = line_total
                                  and payment.currency = line_currency
                                  and payment.method = 'cash'
                                  and payment.status = target_status
                                  and payment.client_id = target_client_id
                                  and payment.membership_id is null
                                  and payment.occurred_at = target_occurred_at
                                  and payment.recorded_at = target_recorded_at
                                  and payment.recorded_by_account_id = target_actor_id
                                  and payment.session_id = target_session_id
                                  and payment.entry_origin = target_entry_origin
                                  and payment.entry_batch_id is not distinct from target_entry_batch_id)
                        into matching_payment_count
                        from bodylife.payments payment
                        where payment.negative_closure_id = target_closure_id
                          and payment.payment_context = 'negative_closure';

                        if payment_count <> 1 or matching_payment_count <> 1 then
                            raise exception using
                                errcode = '23514',
                                constraint = 'ck_negative_closure_exact_payment',
                                message = 'One-off negative closure requires one exact lifecycle-matched cash Payment.';
                        end if;
                    else
                        select count(*)
                        into malformed_item_count
                        from bodylife.membership_negative_closure_items item
                        left join bodylife.visit_consumptions new_consumption
                            on new_consumption.id = item.new_consumption_id
                           and new_consumption.client_id = item.client_id
                        where item.negative_closure_id = target_closure_id
                          and (item.closure_line_id is not null
                            or item.covering_membership_id is distinct from target_covering_membership_id
                            or item.new_consumption_id is null
                            or new_consumption.id is null
                            or new_consumption.visit_id <> item.visit_id
                            or new_consumption.membership_id <> item.covering_membership_id
                            or new_consumption.consumption_type <> 'negative_coverage'
                            or new_consumption.source_fact_type <> 'negative_closure_item'
                            or new_consumption.source_fact_id <> item.id
                            or new_consumption.status <> case
                                when target_status = 'active' then 'active'
                                else 'canceled'
                            end);

                        select
                            visits_limit_snapshot,
                            start_date,
                            status,
                            issuance_mode
                        into
                            covering_visits_limit,
                            covering_start_date,
                            covering_status,
                            covering_mode
                        from bodylife.issued_memberships
                        where id = target_covering_membership_id
                          and client_id = target_client_id;

                        select (occurred_at at time zone 'Europe/Kyiv')::date
                        into oldest_visit_date
                        from bodylife.visits
                        where id = target_oldest_visit_id
                          and client_id = target_client_id;

                        if target_covering_membership_id is null
                            or line_count <> 0
                            or payment_count <> 0
                            or malformed_item_count <> 0
                            or covering_visits_limit is null
                            or target_visits_count > covering_visits_limit
                            or covering_start_date is distinct from oldest_visit_date
                            or covering_mode <> 'sale'
                            or (target_status = 'active' and covering_status <> 'active') then
                            raise exception using
                                errcode = '23514',
                                constraint = 'ck_negative_closure_membership_allocation',
                                message = 'New-Membership coverage requires valid Visit allocations within the covering Membership limit.';
                        end if;
                    end if;

                    select
                        count(*),
                        count(*) filter (where
                            (target_status = 'canceled'
                                and correction.mode = 'cancel'
                                and correction.replacement_closure_id is null)
                            or (target_status = 'replaced'
                                and correction.mode = 'replace'
                                and correction.replacement_closure_id is not null
                                and exists (
                                    select 1
                                    from bodylife.membership_negative_closures replacement
                                    where replacement.id = correction.replacement_closure_id
                                      and replacement.client_id = target_client_id
                                      and replacement.status = 'active')))
                    into correction_count, matching_correction_count
                    from bodylife.membership_negative_closure_corrections correction
                    where correction.original_closure_id = target_closure_id;

                    if (target_status = 'active' and correction_count <> 0)
                        or (target_status <> 'active'
                            and (correction_count <> 1 or matching_correction_count <> 1)) then
                        raise exception using
                            errcode = '23514',
                            constraint = 'ck_negative_closure_correction_lifecycle',
                            message = 'Negative closure lifecycle status requires its matching correction fact.';
                    end if;
                end
                $function$;

                create function bodylife.enforce_membership_negative_closure()
                returns trigger
                language plpgsql
                as $function$
                declare
                    candidate_closure_id uuid;
                begin
                    if tg_table_name = 'membership_negative_closures' then
                        perform bodylife.assert_membership_negative_closure(
                            case when tg_op = 'DELETE' then old.id else new.id end);
                    elsif tg_table_name = 'membership_negative_closure_lines' then
                        if tg_op <> 'INSERT' then
                            perform bodylife.assert_membership_negative_closure(old.negative_closure_id);
                        end if;
                        if tg_op <> 'DELETE' then
                            perform bodylife.assert_membership_negative_closure(new.negative_closure_id);
                        end if;
                    elsif tg_table_name = 'membership_negative_closure_items' then
                        if tg_op <> 'INSERT' then
                            perform bodylife.assert_membership_negative_closure(old.negative_closure_id);
                        end if;
                        if tg_op <> 'DELETE' then
                            perform bodylife.assert_membership_negative_closure(new.negative_closure_id);
                        end if;
                    elsif tg_table_name = 'payments' then
                        if tg_op <> 'INSERT' and old.payment_context = 'negative_closure' then
                            perform bodylife.assert_membership_negative_closure(old.negative_closure_id);
                        end if;
                        if tg_op <> 'DELETE' and new.payment_context = 'negative_closure' then
                            perform bodylife.assert_membership_negative_closure(new.negative_closure_id);
                        end if;
                    elsif tg_table_name = 'membership_negative_closure_corrections' then
                        if tg_op <> 'INSERT' then
                            perform bodylife.assert_membership_negative_closure(old.original_closure_id);
                            perform bodylife.assert_membership_negative_closure(old.replacement_closure_id);
                        end if;
                        if tg_op <> 'DELETE' then
                            perform bodylife.assert_membership_negative_closure(new.original_closure_id);
                            perform bodylife.assert_membership_negative_closure(new.replacement_closure_id);
                        end if;
                    elsif tg_table_name = 'visit_consumptions' then
                        for candidate_closure_id in
                            select distinct item.negative_closure_id
                            from bodylife.membership_negative_closure_items item
                            where (tg_op <> 'INSERT' and (
                                    item.old_consumption_id = old.id
                                    or item.new_consumption_id = old.id))
                               or (tg_op <> 'DELETE' and (
                                    item.old_consumption_id = new.id
                                    or item.new_consumption_id = new.id))
                        loop
                            perform bodylife.assert_membership_negative_closure(candidate_closure_id);
                        end loop;
                    elsif tg_table_name = 'visits' then
                        for candidate_closure_id in
                            select distinct item.negative_closure_id
                            from bodylife.membership_negative_closure_items item
                            where (tg_op <> 'INSERT' and item.visit_id = old.id)
                               or (tg_op <> 'DELETE' and item.visit_id = new.id)
                        loop
                            perform bodylife.assert_membership_negative_closure(candidate_closure_id);
                        end loop;
                    elsif tg_table_name = 'issued_memberships' then
                        for candidate_closure_id in
                            select distinct item.negative_closure_id
                            from bodylife.membership_negative_closure_items item
                            where (tg_op <> 'INSERT' and (
                                    item.source_membership_id = old.id
                                    or item.covering_membership_id = old.id))
                               or (tg_op <> 'DELETE' and (
                                    item.source_membership_id = new.id
                                    or item.covering_membership_id = new.id))
                        loop
                            perform bodylife.assert_membership_negative_closure(candidate_closure_id);
                        end loop;
                    end if;
                    return null;
                end
                $function$;

                create constraint trigger ck_negative_closures_consistent
                    after insert or update or delete
                    on bodylife.membership_negative_closures
                    deferrable initially deferred
                    for each row execute function bodylife.enforce_membership_negative_closure();

                create constraint trigger ck_negative_closure_lines_consistent
                    after insert or update or delete
                    on bodylife.membership_negative_closure_lines
                    deferrable initially deferred
                    for each row execute function bodylife.enforce_membership_negative_closure();

                create constraint trigger ck_negative_closure_items_consistent
                    after insert or update or delete
                    on bodylife.membership_negative_closure_items
                    deferrable initially deferred
                    for each row execute function bodylife.enforce_membership_negative_closure();

                create constraint trigger ck_negative_closure_payments_consistent
                    after insert or update or delete
                    on bodylife.payments
                    deferrable initially deferred
                    for each row execute function bodylife.enforce_membership_negative_closure();

                create constraint trigger ck_negative_closure_corrections_consistent
                    after insert or update or delete
                    on bodylife.membership_negative_closure_corrections
                    deferrable initially deferred
                    for each row execute function bodylife.enforce_membership_negative_closure();

                create constraint trigger ck_negative_closure_consumptions_consistent
                    after insert or update or delete
                    on bodylife.visit_consumptions
                    deferrable initially deferred
                    for each row execute function bodylife.enforce_membership_negative_closure();

                create constraint trigger ck_negative_closure_visits_consistent
                    after update or delete
                    on bodylife.visits
                    deferrable initially deferred
                    for each row execute function bodylife.enforce_membership_negative_closure();

                create constraint trigger ck_negative_closure_memberships_consistent
                    after update or delete
                    on bodylife.issued_memberships
                    deferrable initially deferred
                    for each row execute function bodylife.enforce_membership_negative_closure();

                create function bodylife.protect_membership_negative_closure_source()
                returns trigger
                language plpgsql
                as $function$
                begin
                    if tg_table_name = 'membership_negative_closures' then
                        if row(
                            old.id, old.client_id, old.closure_type,
                            old.covering_membership_id, old.oldest_open_negative_visit_id,
                            old.visits_count, old.comment, old.occurred_at, old.recorded_at,
                            old.recorded_by_account_id, old.session_id, old.entry_origin,
                            old.entry_batch_id, old.idempotency_key)
                            is distinct from row(
                            new.id, new.client_id, new.closure_type,
                            new.covering_membership_id, new.oldest_open_negative_visit_id,
                            new.visits_count, new.comment, new.occurred_at, new.recorded_at,
                            new.recorded_by_account_id, new.session_id, new.entry_origin,
                            new.entry_batch_id, new.idempotency_key)
                            or old.status <> 'active'
                            or new.status not in ('canceled', 'replaced') then
                            raise exception using
                                errcode = '23514',
                                constraint = 'ck_negative_closures_immutable_source',
                                message = 'Negative closure source fields are immutable and lifecycle changes are one-way.';
                        end if;
                    elsif tg_table_name = 'membership_negative_closure_items' then
                        if row(
                            old.id, old.negative_closure_id, old.client_id,
                            old.closure_line_id, old.sequence, old.visit_id,
                            old.source_membership_id, old.old_consumption_id,
                            old.covering_membership_id, old.new_consumption_id)
                            is distinct from row(
                            new.id, new.negative_closure_id, new.client_id,
                            new.closure_line_id, new.sequence, new.visit_id,
                            new.source_membership_id, new.old_consumption_id,
                            new.covering_membership_id, new.new_consumption_id)
                            or old.status <> 'active'
                            or new.status not in ('canceled', 'replaced') then
                            raise exception using
                                errcode = '23514',
                                constraint = 'ck_negative_closure_items_immutable_source',
                                message = 'Negative closure item source fields are immutable and lifecycle changes are one-way.';
                        end if;
                    else
                        raise exception using
                            errcode = '23514',
                            constraint = 'ck_negative_closure_history_immutable',
                            message = 'Negative closure history rows are immutable.';
                    end if;
                    return new;
                end
                $function$;

                create trigger ck_negative_closures_immutable_source
                    before update on bodylife.membership_negative_closures
                    for each row execute function bodylife.protect_membership_negative_closure_source();

                create trigger ck_negative_closure_items_immutable_source
                    before update on bodylife.membership_negative_closure_items
                    for each row execute function bodylife.protect_membership_negative_closure_source();

                create trigger ck_negative_closure_lines_immutable_source
                    before update on bodylife.membership_negative_closure_lines
                    for each row execute function bodylife.protect_membership_negative_closure_source();

                create trigger ck_negative_closure_corrections_immutable_source
                    before update on bodylife.membership_negative_closure_corrections
                    for each row execute function bodylife.protect_membership_negative_closure_source();

                update bodylife.membership_state_cache
                set recalculation_version = 8
                where recalculation_version = 7;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                drop trigger if exists ck_negative_closure_memberships_consistent
                    on bodylife.issued_memberships;
                drop trigger if exists ck_negative_closure_visits_consistent
                    on bodylife.visits;
                drop trigger if exists ck_negative_closure_consumptions_consistent
                    on bodylife.visit_consumptions;
                drop trigger if exists ck_negative_closure_corrections_consistent
                    on bodylife.membership_negative_closure_corrections;
                drop trigger if exists ck_negative_closure_payments_consistent
                    on bodylife.payments;
                drop trigger if exists ck_negative_closure_items_consistent
                    on bodylife.membership_negative_closure_items;
                drop trigger if exists ck_negative_closure_lines_consistent
                    on bodylife.membership_negative_closure_lines;
                drop trigger if exists ck_negative_closures_consistent
                    on bodylife.membership_negative_closures;
                drop trigger if exists ck_negative_closure_corrections_immutable_source
                    on bodylife.membership_negative_closure_corrections;
                drop trigger if exists ck_negative_closure_lines_immutable_source
                    on bodylife.membership_negative_closure_lines;
                drop trigger if exists ck_negative_closure_items_immutable_source
                    on bodylife.membership_negative_closure_items;
                drop trigger if exists ck_negative_closures_immutable_source
                    on bodylife.membership_negative_closures;
                drop function if exists bodylife.protect_membership_negative_closure_source();
                drop function if exists bodylife.enforce_membership_negative_closure();
                drop function if exists bodylife.assert_membership_negative_closure(uuid);

                update bodylife.membership_state_cache
                set recalculation_version = 7
                where recalculation_version = 8;
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_payments_negative_closures_closure_client",
                schema: "bodylife",
                table: "payments");

            migrationBuilder.DropTable(
                name: "membership_negative_closure_corrections",
                schema: "bodylife");

            migrationBuilder.DropTable(
                name: "membership_negative_closure_items",
                schema: "bodylife");

            migrationBuilder.DropTable(
                name: "membership_negative_closure_lines",
                schema: "bodylife");

            migrationBuilder.DropTable(
                name: "membership_negative_closures",
                schema: "bodylife");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_visits_id_client_id",
                schema: "bodylife",
                table: "visits");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_visit_consumptions_id_client_id",
                schema: "bodylife",
                table: "visit_consumptions");

            migrationBuilder.DropIndex(
                name: "ux_visit_consumptions_active_negative_coverage_source",
                schema: "bodylife",
                table: "visit_consumptions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_visit_consumptions_consumption_type",
                schema: "bodylife",
                table: "visit_consumptions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_visit_consumptions_source_fact_identity",
                schema: "bodylife",
                table: "visit_consumptions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_visit_consumptions_source_fact_type",
                schema: "bodylife",
                table: "visit_consumptions");

            migrationBuilder.DropIndex(
                name: "IX_payments_negative_closure_id_client_id",
                schema: "bodylife",
                table: "payments");

            migrationBuilder.DropIndex(
                name: "ux_payments_active_negative_closure",
                schema: "bodylife",
                table: "payments");

            migrationBuilder.DropCheckConstraint(
                name: "ck_payments_negative_closure_context",
                schema: "bodylife",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "negative_closure_id",
                schema: "bodylife",
                table: "payments");

            migrationBuilder.AddCheckConstraint(
                name: "ck_visit_consumptions_consumption_type",
                schema: "bodylife",
                table: "visit_consumptions",
                sql: "consumption_type = 'counted'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_visit_consumptions_source_fact_identity",
                schema: "bodylife",
                table: "visit_consumptions",
                sql: "source_fact_id = visit_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_visit_consumptions_source_fact_type",
                schema: "bodylife",
                table: "visit_consumptions",
                sql: "source_fact_type = 'visit'");
        }
    }
}
