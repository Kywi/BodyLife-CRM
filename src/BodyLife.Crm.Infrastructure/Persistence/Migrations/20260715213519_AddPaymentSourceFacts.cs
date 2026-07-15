using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BodyLife.Crm.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentSourceFacts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "payments",
                schema: "bodylife",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    membership_id = table.Column<Guid>(type: "uuid", nullable: true),
                    amount = table.Column<decimal>(type: "numeric", nullable: false),
                    currency = table.Column<string>(type: "text", nullable: false),
                    method = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    payment_context = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recorded_by_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    entry_origin = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    entry_batch_id = table.Column<Guid>(type: "uuid", nullable: true),
                    comment = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payments", x => x.id);
                    table.UniqueConstraint("AK_payments_id_client_id", x => new { x.id, x.client_id });
                    table.CheckConstraint("ck_payments_amount_positive", "amount > 0");
                    table.CheckConstraint("ck_payments_comment_not_empty", "comment is null or length(btrim(comment)) > 0");
                    table.CheckConstraint("ck_payments_currency_canonical", "length(btrim(currency)) > 0 and currency = upper(btrim(currency))");
                    table.CheckConstraint("ck_payments_entry_origin", "entry_origin in ('normal', 'manual_backfill', 'paper_fallback', 'future_import')");
                    table.CheckConstraint("ck_payments_method", "method = 'cash'");
                    table.CheckConstraint("ck_payments_payment_context", "payment_context in ('membership_sale', 'one_off', 'trial', 'negative_closure', 'other')");
                    table.CheckConstraint("ck_payments_status", "status in ('active', 'canceled', 'replaced')");
                    table.ForeignKey(
                        name: "FK_payments_accounts_recorded_by_account_id",
                        column: x => x.recorded_by_account_id,
                        principalSchema: "bodylife",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_payments_clients_client_id",
                        column: x => x.client_id,
                        principalSchema: "bodylife",
                        principalTable: "clients",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_payments_issued_memberships_membership_client",
                        columns: x => new { x.membership_id, x.client_id },
                        principalSchema: "bodylife",
                        principalTable: "issued_memberships",
                        principalColumns: new[] { "id", "client_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_payments_sessions_session_id",
                        column: x => x.session_id,
                        principalSchema: "bodylife",
                        principalTable: "sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "payment_cancellations",
                schema: "bodylife",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    payment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recorded_by_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    entry_origin = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    entry_batch_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payment_cancellations", x => x.id);
                    table.CheckConstraint("ck_payment_cancellations_entry_origin", "entry_origin in ('normal', 'manual_backfill', 'paper_fallback', 'future_import')");
                    table.CheckConstraint("ck_payment_cancellations_reason_not_empty", "length(btrim(reason)) > 0");
                    table.ForeignKey(
                        name: "FK_payment_cancellations_accounts_recorded_by_account_id",
                        column: x => x.recorded_by_account_id,
                        principalSchema: "bodylife",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_payment_cancellations_payments_payment_id",
                        column: x => x.payment_id,
                        principalSchema: "bodylife",
                        principalTable: "payments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_payment_cancellations_sessions_session_id",
                        column: x => x.session_id,
                        principalSchema: "bodylife",
                        principalTable: "sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "payment_corrections",
                schema: "bodylife",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    original_payment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    replacement_payment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    changed_fields = table.Column<string>(type: "jsonb", nullable: false),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recorded_by_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    entry_origin = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    entry_batch_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payment_corrections", x => x.id);
                    table.CheckConstraint("ck_payment_corrections_changed_fields", "jsonb_typeof(changed_fields) = 'array' and jsonb_array_length(changed_fields) > 0");
                    table.CheckConstraint("ck_payment_corrections_distinct_payments", "original_payment_id <> replacement_payment_id");
                    table.CheckConstraint("ck_payment_corrections_entry_origin", "entry_origin in ('normal', 'manual_backfill', 'paper_fallback', 'future_import')");
                    table.CheckConstraint("ck_payment_corrections_reason_not_empty", "length(btrim(reason)) > 0");
                    table.ForeignKey(
                        name: "FK_payment_corrections_accounts_recorded_by_account_id",
                        column: x => x.recorded_by_account_id,
                        principalSchema: "bodylife",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_payment_corrections_payments_original_client",
                        columns: x => new { x.original_payment_id, x.client_id },
                        principalSchema: "bodylife",
                        principalTable: "payments",
                        principalColumns: new[] { "id", "client_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_payment_corrections_payments_replacement_client",
                        columns: x => new { x.replacement_payment_id, x.client_id },
                        principalSchema: "bodylife",
                        principalTable: "payments",
                        principalColumns: new[] { "id", "client_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_payment_corrections_sessions_session_id",
                        column: x => x.session_id,
                        principalSchema: "bodylife",
                        principalTable: "sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_payment_cancellations_recorded_by_account_id",
                schema: "bodylife",
                table: "payment_cancellations",
                column: "recorded_by_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_payment_cancellations_session_id",
                schema: "bodylife",
                table: "payment_cancellations",
                column: "session_id");

            migrationBuilder.CreateIndex(
                name: "ix_payment_cancellations_timeline",
                schema: "bodylife",
                table: "payment_cancellations",
                columns: new[] { "occurred_at", "recorded_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ux_payment_cancellations_payment_id",
                schema: "bodylife",
                table: "payment_cancellations",
                column: "payment_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_payment_corrections_client_timeline",
                schema: "bodylife",
                table: "payment_corrections",
                columns: new[] { "client_id", "occurred_at", "recorded_at" },
                descending: new[] { false, true, true });

            migrationBuilder.CreateIndex(
                name: "ix_payment_corrections_recorded_by_account_id",
                schema: "bodylife",
                table: "payment_corrections",
                column: "recorded_by_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_payment_corrections_session_id",
                schema: "bodylife",
                table: "payment_corrections",
                column: "session_id");

            migrationBuilder.CreateIndex(
                name: "ux_payment_corrections_original_payment_id",
                schema: "bodylife",
                table: "payment_corrections",
                columns: new[] { "original_payment_id", "client_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_payment_corrections_replacement_payment_id",
                schema: "bodylife",
                table: "payment_corrections",
                columns: new[] { "replacement_payment_id", "client_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_payments_active_daily_report",
                schema: "bodylife",
                table: "payments",
                columns: new[] { "occurred_at", "method", "client_id" },
                filter: "status = 'active'")
                .Annotation("Npgsql:IndexInclude", new[] { "amount" });

            migrationBuilder.CreateIndex(
                name: "ix_payments_client_timeline",
                schema: "bodylife",
                table: "payments",
                columns: new[] { "client_id", "occurred_at", "recorded_at" },
                descending: new[] { false, true, true });

            migrationBuilder.CreateIndex(
                name: "ix_payments_daily_source",
                schema: "bodylife",
                table: "payments",
                columns: new[] { "occurred_at", "status", "method", "client_id" })
                .Annotation("Npgsql:IndexInclude", new[] { "amount" });

            migrationBuilder.CreateIndex(
                name: "ix_payments_membership_timeline",
                schema: "bodylife",
                table: "payments",
                columns: new[] { "membership_id", "client_id", "occurred_at" },
                descending: new[] { false, false, true },
                filter: "membership_id is not null");

            migrationBuilder.CreateIndex(
                name: "ix_payments_recorded_by_account_id",
                schema: "bodylife",
                table: "payments",
                column: "recorded_by_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_payments_session_id",
                schema: "bodylife",
                table: "payments",
                column: "session_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "payment_cancellations",
                schema: "bodylife");

            migrationBuilder.DropTable(
                name: "payment_corrections",
                schema: "bodylife");

            migrationBuilder.DropTable(
                name: "payments",
                schema: "bodylife");
        }
    }
}
