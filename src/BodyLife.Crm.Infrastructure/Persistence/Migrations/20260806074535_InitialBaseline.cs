using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BodyLife.Crm.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialBaseline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "bodylife");

            migrationBuilder.CreateTable(
                name: "accounts",
                schema: "bodylife",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    display_name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    account_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    role = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deactivated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accounts", x => x.id);
                    table.CheckConstraint("ck_accounts_account_type", "account_type in ('owner', 'named_admin', 'shared_reception_admin')");
                    table.CheckConstraint("ck_accounts_account_type_role", "(account_type = 'owner' and role = 'owner')\nor (account_type in ('named_admin', 'shared_reception_admin') and role = 'admin')");
                    table.CheckConstraint("ck_accounts_active_deactivated_at", "(is_active and deactivated_at is null) or (not is_active and deactivated_at is not null)");
                    table.CheckConstraint("ck_accounts_deactivated_at_after_created", "deactivated_at is null or deactivated_at >= created_at");
                    table.CheckConstraint("ck_accounts_display_name_not_empty", "length(btrim(display_name)) > 0");
                    table.CheckConstraint("ck_accounts_role", "role in ('owner', 'admin')");
                });

            migrationBuilder.CreateTable(
                name: "business_audit_entries",
                schema: "bodylife",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    action_type = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    entity_type = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    related_entity_refs = table.Column<string>(type: "jsonb", nullable: false),
                    actor_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_account_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    actor_role = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    device_label = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    comment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    before_summary = table.Column<string>(type: "jsonb", nullable: false),
                    after_summary = table.Column<string>(type: "jsonb", nullable: false),
                    request_correlation_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    entry_origin = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    changed_after_close = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_business_audit_entries", x => x.id);
                    table.CheckConstraint("ck_business_audit_entries_action_type_not_empty", "length(btrim(action_type)) > 0");
                    table.CheckConstraint("ck_business_audit_entries_actor_account_type", "actor_account_type in ('owner', 'named_admin', 'shared_reception_admin')");
                    table.CheckConstraint("ck_business_audit_entries_actor_role", "actor_role in ('owner', 'admin')");
                    table.CheckConstraint("ck_business_audit_entries_correlation_not_empty", "length(btrim(request_correlation_id)) > 0");
                    table.CheckConstraint("ck_business_audit_entries_entity_type_not_empty", "length(btrim(entity_type)) > 0");
                    table.CheckConstraint("ck_business_audit_entries_entry_origin", "entry_origin in ('normal', 'manual_backfill', 'paper_fallback', 'future_import')");
                });

            migrationBuilder.CreateTable(
                name: "command_idempotency_keys",
                schema: "bodylife",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    command_name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    request_correlation_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    actor_role = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    account_kind = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: true),
                    device_label = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    entry_origin = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    primary_entity_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reread_target_id = table.Column<Guid>(type: "uuid", nullable: true),
                    audit_entry_id = table.Column<Guid>(type: "uuid", nullable: true),
                    result_fingerprint = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_command_idempotency_keys", x => x.id);
                    table.CheckConstraint("ck_command_idempotency_keys_account_kind_not_empty", "length(btrim(account_kind)) > 0");
                    table.CheckConstraint("ck_command_idempotency_keys_actor_role_not_empty", "length(btrim(actor_role)) > 0");
                    table.CheckConstraint("ck_command_idempotency_keys_command_name_not_empty", "length(btrim(command_name)) > 0");
                    table.CheckConstraint("ck_command_idempotency_keys_completed_after_created", "completed_at is null or completed_at >= created_at");
                    table.CheckConstraint("ck_command_idempotency_keys_completed_status_has_time", "(status = 'started') or completed_at is not null");
                    table.CheckConstraint("ck_command_idempotency_keys_correlation_not_empty", "length(btrim(request_correlation_id)) > 0");
                    table.CheckConstraint("ck_command_idempotency_keys_entry_origin", "entry_origin in ('normal', 'manual_backfill', 'paper_fallback', 'future_import')");
                    table.CheckConstraint("ck_command_idempotency_keys_expires_after_created", "expires_at > created_at");
                    table.CheckConstraint("ck_command_idempotency_keys_key_not_empty", "length(btrim(idempotency_key)) > 0");
                    table.CheckConstraint("ck_command_idempotency_keys_started_not_completed", "(status <> 'started') or completed_at is null");
                    table.CheckConstraint("ck_command_idempotency_keys_status", "status in ('started', 'succeeded', 'failed')");
                });

            migrationBuilder.CreateTable(
                name: "membership_types",
                schema: "bodylife",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    duration_days = table.Column<int>(type: "integer", nullable: false),
                    visits_limit = table.Column<int>(type: "integer", nullable: false),
                    price_amount = table.Column<decimal>(type: "numeric", nullable: false),
                    price_currency = table.Column<string>(type: "text", nullable: false),
                    kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, defaultValue: "ordinary"),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    comment = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deactivated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_membership_types", x => x.id);
                    table.CheckConstraint("ck_membership_types_active_sale_terms", "not is_active or price_amount > 0");
                    table.CheckConstraint("ck_membership_types_comment_not_empty", "comment is null or length(btrim(comment)) > 0");
                    table.CheckConstraint("ck_membership_types_currency_canonical", "length(btrim(price_currency)) > 0 and price_currency = upper(btrim(price_currency))");
                    table.CheckConstraint("ck_membership_types_duration_positive", "duration_days > 0");
                    table.CheckConstraint("ck_membership_types_kind", "kind in ('ordinary', 'one_off')");
                    table.CheckConstraint("ck_membership_types_lifecycle", "(\n    is_active\n    and deactivated_at is null\n)\nor (\n    not is_active\n    and deactivated_at is not null\n    and deactivated_at >= created_at\n    and deactivated_at <= updated_at\n)");
                    table.CheckConstraint("ck_membership_types_name_not_empty", "length(btrim(name)) > 0");
                    table.CheckConstraint("ck_membership_types_one_off_visits", "kind <> 'one_off' or visits_limit = 1");
                    table.CheckConstraint("ck_membership_types_price_non_negative", "price_amount >= 0");
                    table.CheckConstraint("ck_membership_types_updated_after_created", "updated_at >= created_at");
                    table.CheckConstraint("ck_membership_types_visits_non_negative", "visits_limit >= 0");
                });

            migrationBuilder.CreateTable(
                name: "account_credentials",
                schema: "bodylife",
                columns: table => new
                {
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    login_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    normalized_login_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    password_hash = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    password_changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_account_credentials", x => x.account_id);
                    table.CheckConstraint("ck_account_credentials_login_name_not_empty", "length(btrim(login_name)) > 0");
                    table.CheckConstraint("ck_account_credentials_normalized_login_name_not_empty", "length(btrim(normalized_login_name)) > 0");
                    table.CheckConstraint("ck_account_credentials_password_hash_not_empty", "length(btrim(password_hash)) > 0");
                    table.ForeignKey(
                        name: "FK_account_credentials_accounts_account_id",
                        column: x => x.account_id,
                        principalSchema: "bodylife",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "clients",
                schema: "bodylife",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    surname = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    patronymic = table.Column<string>(type: "text", nullable: true),
                    normalized_full_name = table.Column<string>(type: "text", nullable: false),
                    phone_raw = table.Column<string>(type: "text", nullable: true),
                    phone_normalized = table.Column<string>(type: "text", nullable: true),
                    phone_last4 = table.Column<string>(type: "text", nullable: true),
                    comment = table.Column<string>(type: "text", nullable: true),
                    operational_status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_clients", x => x.id);
                    table.CheckConstraint("ck_clients_name_not_empty", "length(btrim(name)) > 0");
                    table.CheckConstraint("ck_clients_normalized_full_name_not_empty", "length(btrim(normalized_full_name)) > 0");
                    table.CheckConstraint("ck_clients_operational_status", "operational_status in ('active', 'inactive')");
                    table.CheckConstraint("ck_clients_patronymic_not_empty", "patronymic is null or length(btrim(patronymic)) > 0");
                    table.CheckConstraint("ck_clients_phone_fields_consistent", "(phone_raw is null and phone_normalized is null and phone_last4 is null)\nor (\n    phone_raw is not null\n    and length(btrim(phone_raw)) > 0\n    and phone_normalized is not null\n    and phone_normalized ~ '^[0-9]{4,}$'\n    and phone_last4 is not null\n    and phone_last4 ~ '^[0-9]{4}$'\n    and phone_last4 = right(phone_normalized, 4)\n)");
                    table.CheckConstraint("ck_clients_surname_not_empty", "length(btrim(surname)) > 0");
                    table.CheckConstraint("ck_clients_updated_after_created", "updated_at >= created_at");
                    table.ForeignKey(
                        name: "FK_clients_accounts_created_by_account_id",
                        column: x => x.created_by_account_id,
                        principalSchema: "bodylife",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

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
                name: "sessions",
                schema: "bodylife",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    device_label = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ended_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sessions", x => x.id);
                    table.CheckConstraint("ck_sessions_ended_at_after_started", "ended_at is null or ended_at >= started_at");
                    table.CheckConstraint("ck_sessions_expires_after_started", "expires_at > started_at");
                    table.CheckConstraint("ck_sessions_last_seen_after_started", "last_seen_at >= started_at");
                    table.ForeignKey(
                        name: "FK_sessions_accounts_account_id",
                        column: x => x.account_id,
                        principalSchema: "bodylife",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "client_card_assignments",
                schema: "bodylife",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    card_number_raw = table.Column<string>(type: "text", nullable: false),
                    card_number_normalized = table.Column<string>(type: "text", nullable: false),
                    assigned_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    assigned_by_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ended_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ended_by_account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    end_reason = table.Column<string>(type: "text", nullable: true),
                    is_current = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_client_card_assignments", x => x.id);
                    table.CheckConstraint("ck_client_card_assignments_ended_after_assigned", "ended_at is null or ended_at >= assigned_at");
                    table.CheckConstraint("ck_client_card_assignments_lifecycle", "(\n    is_current\n    and ended_at is null\n    and ended_by_account_id is null\n    and end_reason is null\n)\nor (\n    not is_current\n    and ended_at is not null\n    and ended_by_account_id is not null\n    and end_reason is not null\n    and length(btrim(end_reason)) > 0\n)");
                    table.CheckConstraint("ck_client_card_assignments_normalized_not_empty", "length(btrim(card_number_normalized)) > 0");
                    table.CheckConstraint("ck_client_card_assignments_raw_not_empty", "length(btrim(card_number_raw)) > 0");
                    table.ForeignKey(
                        name: "FK_client_card_assignments_accounts_assigned_by_account_id",
                        column: x => x.assigned_by_account_id,
                        principalSchema: "bodylife",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_client_card_assignments_accounts_ended_by_account_id",
                        column: x => x.ended_by_account_id,
                        principalSchema: "bodylife",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_client_card_assignments_clients_client_id",
                        column: x => x.client_id,
                        principalSchema: "bodylife",
                        principalTable: "clients",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "duplicate_warning_acknowledgements",
                schema: "bodylife",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    warning_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    matched_client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    acknowledged_by_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    acknowledged_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_duplicate_warning_acknowledgements", x => x.id);
                    table.CheckConstraint("ck_duplicate_warning_acknowledgements_distinct_clients", "client_id <> matched_client_id");
                    table.CheckConstraint("ck_duplicate_warning_acknowledgements_reason_not_empty", "length(btrim(reason)) > 0");
                    table.CheckConstraint("ck_duplicate_warning_acknowledgements_warning_type", "warning_type in ('duplicate_phone', 'similar_name')");
                    table.ForeignKey(
                        name: "fk_duplicate_warning_acks_actor",
                        column: x => x.acknowledged_by_account_id,
                        principalSchema: "bodylife",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_duplicate_warning_acks_client",
                        column: x => x.client_id,
                        principalSchema: "bodylife",
                        principalTable: "clients",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_duplicate_warning_acks_matched_client",
                        column: x => x.matched_client_id,
                        principalSchema: "bodylife",
                        principalTable: "clients",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "issued_memberships",
                schema: "bodylife",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    membership_type_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type_name_snapshot = table.Column<string>(type: "text", nullable: false),
                    duration_days_snapshot = table.Column<int>(type: "integer", nullable: false),
                    visits_limit_snapshot = table.Column<int>(type: "integer", nullable: false),
                    price_amount_snapshot = table.Column<decimal>(type: "numeric", nullable: false),
                    price_currency_snapshot = table.Column<string>(type: "text", nullable: false),
                    issuance_mode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    base_end_date = table.Column<DateOnly>(type: "date", nullable: false),
                    issued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    issued_by_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    entry_origin = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    entry_batch_id = table.Column<Guid>(type: "uuid", nullable: true),
                    comment = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_issued_memberships", x => x.id);
                    table.UniqueConstraint("AK_issued_memberships_id_client_id", x => new { x.id, x.client_id });
                    table.CheckConstraint("ck_issued_memberships_base_end_date", "base_end_date = start_date + (duration_days_snapshot - 1)");
                    table.CheckConstraint("ck_issued_memberships_comment_not_empty", "comment is null or length(btrim(comment)) > 0");
                    table.CheckConstraint("ck_issued_memberships_currency_snapshot_canonical", "length(btrim(price_currency_snapshot)) > 0 and price_currency_snapshot = upper(btrim(price_currency_snapshot))");
                    table.CheckConstraint("ck_issued_memberships_duration_snapshot_positive", "duration_days_snapshot > 0");
                    table.CheckConstraint("ck_issued_memberships_entry_origin", "entry_origin in ('normal', 'manual_backfill', 'paper_fallback', 'future_import')");
                    table.CheckConstraint("ck_issued_memberships_issuance_mode", "issuance_mode in ('sale', 'opening_state')");
                    table.CheckConstraint("ck_issued_memberships_price_snapshot_non_negative", "price_amount_snapshot >= 0");
                    table.CheckConstraint("ck_issued_memberships_status", "status in ('active', 'canceled', 'corrected', 'closed')");
                    table.CheckConstraint("ck_issued_memberships_type_name_snapshot_not_empty", "length(btrim(type_name_snapshot)) > 0");
                    table.CheckConstraint("ck_issued_memberships_visits_snapshot_non_negative", "visits_limit_snapshot >= 0");
                    table.ForeignKey(
                        name: "FK_issued_memberships_accounts_issued_by_account_id",
                        column: x => x.issued_by_account_id,
                        principalSchema: "bodylife",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_issued_memberships_clients_client_id",
                        column: x => x.client_id,
                        principalSchema: "bodylife",
                        principalTable: "clients",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_issued_memberships_membership_types_membership_type_id",
                        column: x => x.membership_type_id,
                        principalSchema: "bodylife",
                        principalTable: "membership_types",
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
                name: "non_working_periods",
                schema: "bodylife",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    end_date = table.Column<DateOnly>(type: "date", nullable: false),
                    reason_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    reason_comment = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_non_working_periods", x => x.id);
                    table.UniqueConstraint("AK_non_working_periods_id_range", x => new { x.id, x.start_date, x.end_date });
                    table.CheckConstraint("ck_non_working_periods_inclusive_range", "start_date <= end_date");
                    table.CheckConstraint("ck_non_working_periods_reason_code_not_empty", "length(btrim(reason_code)) > 0");
                    table.CheckConstraint("ck_non_working_periods_reason_comment_not_empty", "reason_comment is null or length(btrim(reason_comment)) > 0");
                    table.CheckConstraint("ck_non_working_periods_status", "status in ('active', 'canceled', 'corrected')");
                    table.ForeignKey(
                        name: "FK_non_working_periods_accounts_created_by_account_id",
                        column: x => x.created_by_account_id,
                        principalSchema: "bodylife",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_non_working_periods_sessions_session_id",
                        column: x => x.session_id,
                        principalSchema: "bodylife",
                        principalTable: "sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "visits",
                schema: "bodylife",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recorded_by_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    visit_kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    entry_origin = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    entry_batch_id = table.Column<Guid>(type: "uuid", nullable: true),
                    comment = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_visits", x => x.id);
                    table.UniqueConstraint("AK_visits_id_client_id", x => new { x.id, x.client_id });
                    table.UniqueConstraint("AK_visits_id_client_id_visit_kind", x => new { x.id, x.client_id, x.visit_kind });
                    table.CheckConstraint("ck_visits_comment_not_empty", "comment is null or length(btrim(comment)) > 0");
                    table.CheckConstraint("ck_visits_entry_origin", "entry_origin in ('normal', 'manual_backfill', 'paper_fallback', 'future_import')");
                    table.CheckConstraint("ck_visits_status", "status in ('active', 'canceled')");
                    table.CheckConstraint("ck_visits_visit_kind", "visit_kind in ('membership', 'one_off', 'trial')");
                    table.ForeignKey(
                        name: "FK_visits_accounts_recorded_by_account_id",
                        column: x => x.recorded_by_account_id,
                        principalSchema: "bodylife",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_visits_clients_client_id",
                        column: x => x.client_id,
                        principalSchema: "bodylife",
                        principalTable: "clients",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_visits_sessions_session_id",
                        column: x => x.session_id,
                        principalSchema: "bodylife",
                        principalTable: "sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "freezes",
                schema: "bodylife",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    membership_id = table.Column<Guid>(type: "uuid", nullable: false),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    end_date = table.Column<DateOnly>(type: "date", nullable: false),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recorded_by_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    entry_origin = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    entry_batch_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_freezes", x => x.id);
                    table.CheckConstraint("ck_freezes_entry_origin", "entry_origin in ('normal', 'manual_backfill', 'paper_fallback', 'future_import')");
                    table.CheckConstraint("ck_freezes_inclusive_range", "start_date <= end_date");
                    table.CheckConstraint("ck_freezes_reason_not_empty", "length(btrim(reason)) > 0");
                    table.CheckConstraint("ck_freezes_status", "status in ('active', 'canceled')");
                    table.ForeignKey(
                        name: "FK_freezes_accounts_recorded_by_account_id",
                        column: x => x.recorded_by_account_id,
                        principalSchema: "bodylife",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_freezes_issued_memberships_membership_client",
                        columns: x => new { x.membership_id, x.client_id },
                        principalSchema: "bodylife",
                        principalTable: "issued_memberships",
                        principalColumns: new[] { "id", "client_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_freezes_sessions_session_id",
                        column: x => x.session_id,
                        principalSchema: "bodylife",
                        principalTable: "sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "membership_adjustments",
                schema: "bodylife",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    membership_id = table.Column<Guid>(type: "uuid", nullable: false),
                    adjustment_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    days_delta = table.Column<int>(type: "integer", nullable: true),
                    visits_delta = table.Column<int>(type: "integer", nullable: true),
                    money_delta = table.Column<decimal>(type: "numeric", nullable: true),
                    effective_date = table.Column<DateOnly>(type: "date", nullable: false),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recorded_by_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    recorded_session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    entry_origin = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    entry_batch_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_membership_adjustments", x => x.id);
                    table.CheckConstraint("ck_membership_adjustments_adjustment_type_not_empty", "length(btrim(adjustment_type)) > 0");
                    table.CheckConstraint("ck_membership_adjustments_delta_non_zero", "coalesce(days_delta, 0) <> 0 or coalesce(visits_delta, 0) <> 0 or coalesce(money_delta, 0) <> 0");
                    table.CheckConstraint("ck_membership_adjustments_entry_origin", "entry_origin in ('normal', 'manual_backfill', 'paper_fallback', 'future_import')");
                    table.CheckConstraint("ck_membership_adjustments_reason_not_empty", "length(btrim(reason)) > 0");
                    table.CheckConstraint("ck_membership_adjustments_status", "status in ('active', 'canceled', 'corrected')");
                    table.ForeignKey(
                        name: "FK_membership_adjustments_accounts_recorded_by_account_id",
                        column: x => x.recorded_by_account_id,
                        principalSchema: "bodylife",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_membership_adjustments_issued_memberships_membership_id",
                        column: x => x.membership_id,
                        principalSchema: "bodylife",
                        principalTable: "issued_memberships",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_membership_adjustments_sessions_recorded_session_id",
                        column: x => x.recorded_session_id,
                        principalSchema: "bodylife",
                        principalTable: "sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "membership_extension_days",
                schema: "bodylife",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    membership_id = table.Column<Guid>(type: "uuid", nullable: false),
                    extension_date = table.Column<DateOnly>(type: "date", nullable: false),
                    source_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_label = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    recalculated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_membership_extension_days", x => x.id);
                    table.CheckConstraint("ck_membership_extension_days_source_label_not_empty", "length(btrim(source_label)) > 0");
                    table.CheckConstraint("ck_membership_extension_days_source_type_not_empty", "length(btrim(source_type)) > 0");
                    table.ForeignKey(
                        name: "FK_membership_extension_days_issued_memberships_membership_id",
                        column: x => x.membership_id,
                        principalSchema: "bodylife",
                        principalTable: "issued_memberships",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "membership_opening_states",
                schema: "bodylife",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    membership_id = table.Column<Guid>(type: "uuid", nullable: false),
                    opening_as_of_date = table.Column<DateOnly>(type: "date", nullable: false),
                    declared_remaining_visits = table.Column<int>(type: "integer", nullable: false),
                    declared_negative_balance = table.Column<int>(type: "integer", nullable: false),
                    known_effective_end_date = table.Column<DateOnly>(type: "date", nullable: true),
                    known_extension_days = table.Column<int>(type: "integer", nullable: true),
                    source_reference = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recorded_by_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    recorded_session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    entry_origin = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    entry_batch_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_membership_opening_states", x => x.id);
                    table.CheckConstraint("ck_membership_opening_states_entry_origin", "entry_origin in ('manual_backfill', 'paper_fallback', 'future_import')");
                    table.CheckConstraint("ck_membership_opening_states_known_end_not_before_opening", "known_effective_end_date is null or known_effective_end_date >= opening_as_of_date");
                    table.CheckConstraint("ck_membership_opening_states_known_extension_days_non_negative", "known_extension_days is null or known_extension_days >= 0");
                    table.CheckConstraint("ck_membership_opening_states_negative_balance_consistent", "declared_negative_balance = greatest(0::bigint, -(declared_remaining_visits::bigint))");
                    table.CheckConstraint("ck_membership_opening_states_reason_not_empty", "length(btrim(reason)) > 0");
                    table.CheckConstraint("ck_membership_opening_states_source_reference_not_empty", "length(btrim(source_reference)) > 0");
                    table.CheckConstraint("ck_membership_opening_states_status", "status in ('active', 'canceled', 'corrected')");
                    table.ForeignKey(
                        name: "FK_membership_opening_states_accounts_recorded_by_account_id",
                        column: x => x.recorded_by_account_id,
                        principalSchema: "bodylife",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_membership_opening_states_issued_memberships_membership_id",
                        column: x => x.membership_id,
                        principalSchema: "bodylife",
                        principalTable: "issued_memberships",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_membership_opening_states_sessions_recorded_session_id",
                        column: x => x.recorded_session_id,
                        principalSchema: "bodylife",
                        principalTable: "sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "membership_state_cache",
                schema: "bodylife",
                columns: table => new
                {
                    membership_id = table.Column<Guid>(type: "uuid", nullable: false),
                    counted_visits = table.Column<int>(type: "integer", nullable: false),
                    remaining_visits = table.Column<int>(type: "integer", nullable: false),
                    negative_balance = table.Column<int>(type: "integer", nullable: false),
                    first_negative_visit_id = table.Column<Guid>(type: "uuid", nullable: true),
                    first_negative_visit_date = table.Column<DateOnly>(type: "date", nullable: true),
                    extension_days = table.Column<int>(type: "integer", nullable: false),
                    effective_end_date = table.Column<DateOnly>(type: "date", nullable: false),
                    last_counted_visit_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    recalculated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recalculation_version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_membership_state_cache", x => x.membership_id);
                    table.CheckConstraint("ck_membership_state_cache_counted_visits_non_negative", "counted_visits >= 0");
                    table.CheckConstraint("ck_membership_state_cache_extension_days_non_negative", "extension_days >= 0");
                    table.CheckConstraint("ck_membership_state_cache_negative_balance_consistent", "negative_balance = greatest(0::bigint, -(remaining_visits::bigint))");
                    table.CheckConstraint("ck_membership_state_cache_recalculation_version_positive", "recalculation_version > 0");
                    table.ForeignKey(
                        name: "FK_membership_state_cache_issued_memberships_membership_id",
                        column: x => x.membership_id,
                        principalSchema: "bodylife",
                        principalTable: "issued_memberships",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
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

            migrationBuilder.CreateTable(
                name: "non_working_period_applications",
                schema: "bodylife",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    non_working_period_id = table.Column<Guid>(type: "uuid", nullable: false),
                    membership_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    applied_start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    applied_end_date = table.Column<DateOnly>(type: "date", nullable: false),
                    previewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    confirmed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_non_working_period_applications", x => x.id);
                    table.CheckConstraint("ck_non_working_period_applications_inclusive_range", "applied_start_date <= applied_end_date");
                    table.CheckConstraint("ck_non_working_period_applications_preview_order", "previewed_at <= confirmed_at");
                    table.CheckConstraint("ck_non_working_period_applications_status", "status in ('active', 'canceled', 'corrected')");
                    table.ForeignKey(
                        name: "FK_non_working_period_applications_membership_client",
                        columns: x => new { x.membership_id, x.client_id },
                        principalSchema: "bodylife",
                        principalTable: "issued_memberships",
                        principalColumns: new[] { "id", "client_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_non_working_period_applications_period_range",
                        columns: x => new { x.non_working_period_id, x.applied_start_date, x.applied_end_date },
                        principalSchema: "bodylife",
                        principalTable: "non_working_periods",
                        principalColumns: new[] { "id", "start_date", "end_date" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "non_working_period_cancellations",
                schema: "bodylife",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    non_working_period_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recorded_by_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_non_working_period_cancellations", x => x.id);
                    table.CheckConstraint("ck_non_working_period_cancellations_reason_not_empty", "length(btrim(reason)) > 0");
                    table.ForeignKey(
                        name: "FK_non_working_period_cancellations_accounts_recorded_by_accou~",
                        column: x => x.recorded_by_account_id,
                        principalSchema: "bodylife",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_non_working_period_cancellations_period",
                        column: x => x.non_working_period_id,
                        principalSchema: "bodylife",
                        principalTable: "non_working_periods",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_non_working_period_cancellations_sessions_session_id",
                        column: x => x.session_id,
                        principalSchema: "bodylife",
                        principalTable: "sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

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
                name: "visit_cancellations",
                schema: "bodylife",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    visit_id = table.Column<Guid>(type: "uuid", nullable: false),
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
                    table.PrimaryKey("PK_visit_cancellations", x => x.id);
                    table.CheckConstraint("ck_visit_cancellations_entry_origin", "entry_origin in ('normal', 'manual_backfill', 'paper_fallback', 'future_import')");
                    table.CheckConstraint("ck_visit_cancellations_reason_not_empty", "length(btrim(reason)) > 0");
                    table.ForeignKey(
                        name: "FK_visit_cancellations_accounts_recorded_by_account_id",
                        column: x => x.recorded_by_account_id,
                        principalSchema: "bodylife",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_visit_cancellations_sessions_session_id",
                        column: x => x.session_id,
                        principalSchema: "bodylife",
                        principalTable: "sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_visit_cancellations_visits_visit_id",
                        column: x => x.visit_id,
                        principalSchema: "bodylife",
                        principalTable: "visits",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "visit_consumptions",
                schema: "bodylife",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    visit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    visit_kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    membership_id = table.Column<Guid>(type: "uuid", nullable: false),
                    consumption_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    source_fact_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    source_fact_id = table.Column<Guid>(type: "uuid", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recorded_by_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    recorded_session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_visit_consumptions", x => x.id);
                    table.UniqueConstraint("AK_visit_consumptions_id_client_id", x => new { x.id, x.client_id });
                    table.CheckConstraint("ck_visit_consumptions_consumption_type", "consumption_type in ('counted', 'negative_coverage')");
                    table.CheckConstraint("ck_visit_consumptions_source_fact_identity", "consumption_type <> 'counted' or source_fact_id = visit_id");
                    table.CheckConstraint("ck_visit_consumptions_source_fact_type", "(consumption_type = 'counted' and source_fact_type = 'visit') or (consumption_type = 'negative_coverage' and source_fact_type = 'negative_closure_item')");
                    table.CheckConstraint("ck_visit_consumptions_status", "status in ('active', 'canceled')");
                    table.CheckConstraint("ck_visit_consumptions_visit_kind", "visit_kind = 'membership'");
                    table.ForeignKey(
                        name: "FK_visit_consumptions_accounts_recorded_by_account_id",
                        column: x => x.recorded_by_account_id,
                        principalSchema: "bodylife",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_visit_consumptions_issued_memberships_membership_client",
                        columns: x => new { x.membership_id, x.client_id },
                        principalSchema: "bodylife",
                        principalTable: "issued_memberships",
                        principalColumns: new[] { "id", "client_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_visit_consumptions_sessions_recorded_session_id",
                        column: x => x.recorded_session_id,
                        principalSchema: "bodylife",
                        principalTable: "sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_visit_consumptions_visits_visit_client_kind",
                        columns: x => new { x.visit_id, x.client_id, x.visit_kind },
                        principalSchema: "bodylife",
                        principalTable: "visits",
                        principalColumns: new[] { "id", "client_id", "visit_kind" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "freeze_cancellations",
                schema: "bodylife",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    freeze_id = table.Column<Guid>(type: "uuid", nullable: false),
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
                    table.PrimaryKey("PK_freeze_cancellations", x => x.id);
                    table.CheckConstraint("ck_freeze_cancellations_entry_origin", "entry_origin in ('normal', 'manual_backfill', 'paper_fallback', 'future_import')");
                    table.CheckConstraint("ck_freeze_cancellations_reason_not_empty", "length(btrim(reason)) > 0");
                    table.ForeignKey(
                        name: "FK_freeze_cancellations_accounts_recorded_by_account_id",
                        column: x => x.recorded_by_account_id,
                        principalSchema: "bodylife",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_freeze_cancellations_freezes_freeze_id",
                        column: x => x.freeze_id,
                        principalSchema: "bodylife",
                        principalTable: "freezes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_freeze_cancellations_sessions_session_id",
                        column: x => x.session_id,
                        principalSchema: "bodylife",
                        principalTable: "sessions",
                        principalColumn: "id",
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
                name: "payments",
                schema: "bodylife",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    membership_id = table.Column<Guid>(type: "uuid", nullable: true),
                    negative_closure_id = table.Column<Guid>(type: "uuid", nullable: true),
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
                    table.CheckConstraint("ck_payments_membership_sale_membership", "payment_context <> 'membership_sale' or membership_id is not null");
                    table.CheckConstraint("ck_payments_method", "method = 'cash'");
                    table.CheckConstraint("ck_payments_negative_closure_context", "(payment_context = 'negative_closure' and negative_closure_id is not null and membership_id is null) or (payment_context <> 'negative_closure' and negative_closure_id is null)");
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
                        name: "FK_payments_negative_closures_closure_client",
                        columns: x => new { x.negative_closure_id, x.client_id },
                        principalSchema: "bodylife",
                        principalTable: "membership_negative_closures",
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

            migrationBuilder.CreateTable(
                name: "issued_membership_sale_corrections",
                schema: "bodylife",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    original_membership_id = table.Column<Guid>(type: "uuid", nullable: false),
                    original_payment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    replacement_membership_id = table.Column<Guid>(type: "uuid", nullable: true),
                    replacement_payment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    correction_mode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recorded_by_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    entry_origin = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    entry_batch_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    dependency_token = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_issued_membership_sale_corrections", x => x.id);
                    table.CheckConstraint("ck_issued_membership_sale_corrections_distinct_memberships", "replacement_membership_id is null or replacement_membership_id <> original_membership_id");
                    table.CheckConstraint("ck_issued_membership_sale_corrections_distinct_payments", "replacement_payment_id is null or replacement_payment_id <> original_payment_id");
                    table.CheckConstraint("ck_issued_membership_sale_corrections_mode", "correction_mode in ('cancel', 'replace')");
                    table.CheckConstraint("ck_issued_membership_sale_corrections_origin", "entry_origin in ('normal', 'paper_fallback')");
                    table.CheckConstraint("ck_issued_membership_sale_corrections_origin_batch", "(entry_origin = 'normal' and entry_batch_id is null) or (entry_origin = 'paper_fallback' and entry_batch_id is not null)");
                    table.CheckConstraint("ck_issued_membership_sale_corrections_reason", "length(btrim(reason)) > 0");
                    table.CheckConstraint("ck_issued_membership_sale_corrections_shape", "(correction_mode = 'cancel' and replacement_membership_id is null and replacement_payment_id is null) or (correction_mode = 'replace' and replacement_membership_id is not null and replacement_payment_id is not null)");
                    table.CheckConstraint("ck_issued_membership_sale_corrections_status", "status = 'active'");
                    table.CheckConstraint("ck_issued_membership_sale_corrections_token", "length(btrim(dependency_token)) > 0");
                    table.ForeignKey(
                        name: "FK_issued_membership_sale_corrections_accounts_recorded_by_acc~",
                        column: x => x.recorded_by_account_id,
                        principalSchema: "bodylife",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_issued_membership_sale_corrections_clients_client_id",
                        column: x => x.client_id,
                        principalSchema: "bodylife",
                        principalTable: "clients",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_issued_membership_sale_corrections_sessions_session_id",
                        column: x => x.session_id,
                        principalSchema: "bodylife",
                        principalTable: "sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_issued_sale_corrections_original_membership_client",
                        columns: x => new { x.original_membership_id, x.client_id },
                        principalSchema: "bodylife",
                        principalTable: "issued_memberships",
                        principalColumns: new[] { "id", "client_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_issued_sale_corrections_original_payment_client",
                        columns: x => new { x.original_payment_id, x.client_id },
                        principalSchema: "bodylife",
                        principalTable: "payments",
                        principalColumns: new[] { "id", "client_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_issued_sale_corrections_replacement_membership_client",
                        columns: x => new { x.replacement_membership_id, x.client_id },
                        principalSchema: "bodylife",
                        principalTable: "issued_memberships",
                        principalColumns: new[] { "id", "client_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_issued_sale_corrections_replacement_payment_client",
                        columns: x => new { x.replacement_payment_id, x.client_id },
                        principalSchema: "bodylife",
                        principalTable: "payments",
                        principalColumns: new[] { "id", "client_id" },
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

            migrationBuilder.CreateTable(
                name: "membership_replacement_dependency_items",
                schema: "bodylife",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    sale_correction_id = table.Column<Guid>(type: "uuid", nullable: false),
                    dependency_type = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                    original_fact_id = table.Column<Guid>(type: "uuid", nullable: false),
                    replacement_fact_id = table.Column<Guid>(type: "uuid", nullable: true),
                    validation_summary = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_membership_replacement_dependency_items", x => x.id);
                    table.CheckConstraint("ck_membership_replacement_dependency_items_distinct_facts", "replacement_fact_id is null or replacement_fact_id <> original_fact_id");
                    table.CheckConstraint("ck_membership_replacement_dependency_items_status", "status = 'validated'");
                    table.CheckConstraint("ck_membership_replacement_dependency_items_type", "dependency_type in ('visit', 'freeze', 'non_working_day_application', 'negative_coverage')");
                    table.CheckConstraint("ck_membership_replacement_dependency_items_validation", "length(btrim(validation_summary)) > 0");
                    table.ForeignKey(
                        name: "FK_membership_replacement_dependency_items_issued_membership_s~",
                        column: x => x.sale_correction_id,
                        principalSchema: "bodylife",
                        principalTable: "issued_membership_sale_corrections",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ux_account_credentials_normalized_login_name",
                schema: "bodylife",
                table: "account_credentials",
                column: "normalized_login_name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_accounts_active_type",
                schema: "bodylife",
                table: "accounts",
                columns: new[] { "is_active", "account_type" });

            migrationBuilder.CreateIndex(
                name: "ux_accounts_single_owner",
                schema: "bodylife",
                table: "accounts",
                column: "account_type",
                unique: true,
                filter: "account_type = 'owner'");

            migrationBuilder.CreateIndex(
                name: "ix_business_audit_entries_actor_timeline",
                schema: "bodylife",
                table: "business_audit_entries",
                columns: new[] { "actor_account_id", "recorded_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_business_audit_entries_entity_timeline",
                schema: "bodylife",
                table: "business_audit_entries",
                columns: new[] { "entity_type", "entity_id", "recorded_at" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "ix_business_audit_entries_recorded_timeline",
                schema: "bodylife",
                table: "business_audit_entries",
                columns: new[] { "recorded_at", "id" },
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "ix_business_audit_entries_related_entity_refs",
                schema: "bodylife",
                table: "business_audit_entries",
                column: "related_entity_refs")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "jsonb_path_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_client_card_assignments_assigned_by_account_id",
                schema: "bodylife",
                table: "client_card_assignments",
                column: "assigned_by_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_client_card_assignments_client_history",
                schema: "bodylife",
                table: "client_card_assignments",
                columns: new[] { "client_id", "assigned_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_client_card_assignments_ended_by_account_id",
                schema: "bodylife",
                table: "client_card_assignments",
                column: "ended_by_account_id");

            migrationBuilder.CreateIndex(
                name: "ux_client_card_assignments_current_card",
                schema: "bodylife",
                table: "client_card_assignments",
                column: "card_number_normalized",
                unique: true,
                filter: "is_current");

            migrationBuilder.CreateIndex(
                name: "ux_client_card_assignments_current_client",
                schema: "bodylife",
                table: "client_card_assignments",
                column: "client_id",
                unique: true,
                filter: "is_current");

            migrationBuilder.CreateIndex(
                name: "ix_clients_created_by_account_id",
                schema: "bodylife",
                table: "clients",
                column: "created_by_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_clients_normalized_full_name",
                schema: "bodylife",
                table: "clients",
                column: "normalized_full_name");

            migrationBuilder.CreateIndex(
                name: "ix_clients_phone_last4_status",
                schema: "bodylife",
                table: "clients",
                columns: new[] { "phone_last4", "operational_status" },
                filter: "phone_last4 is not null");

            migrationBuilder.CreateIndex(
                name: "ix_clients_phone_normalized",
                schema: "bodylife",
                table: "clients",
                column: "phone_normalized",
                filter: "phone_normalized is not null");

            migrationBuilder.CreateIndex(
                name: "ix_command_idempotency_keys_expires_at",
                schema: "bodylife",
                table: "command_idempotency_keys",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "ux_command_idempotency_keys_command_key",
                schema: "bodylife",
                table: "command_idempotency_keys",
                columns: new[] { "command_name", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_duplicate_warning_acks_actor",
                schema: "bodylife",
                table: "duplicate_warning_acknowledgements",
                column: "acknowledged_by_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_duplicate_warning_acks_client_timeline",
                schema: "bodylife",
                table: "duplicate_warning_acknowledgements",
                columns: new[] { "client_id", "acknowledged_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_duplicate_warning_acks_match_timeline",
                schema: "bodylife",
                table: "duplicate_warning_acknowledgements",
                columns: new[] { "matched_client_id", "acknowledged_at" },
                descending: new[] { false, true });

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

            migrationBuilder.CreateIndex(
                name: "ix_freeze_cancellations_recorded_by_account_id",
                schema: "bodylife",
                table: "freeze_cancellations",
                column: "recorded_by_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_freeze_cancellations_session_id",
                schema: "bodylife",
                table: "freeze_cancellations",
                column: "session_id");

            migrationBuilder.CreateIndex(
                name: "ix_freeze_cancellations_timeline",
                schema: "bodylife",
                table: "freeze_cancellations",
                columns: new[] { "occurred_at", "recorded_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ux_freeze_cancellations_freeze_id",
                schema: "bodylife",
                table: "freeze_cancellations",
                column: "freeze_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_freezes_client_timeline",
                schema: "bodylife",
                table: "freezes",
                columns: new[] { "client_id", "recorded_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_freezes_membership_id_client_id",
                schema: "bodylife",
                table: "freezes",
                columns: new[] { "membership_id", "client_id" });

            migrationBuilder.CreateIndex(
                name: "ix_freezes_membership_status_range",
                schema: "bodylife",
                table: "freezes",
                columns: new[] { "membership_id", "status", "start_date", "end_date" });

            migrationBuilder.CreateIndex(
                name: "ix_freezes_recorded_by_account_id",
                schema: "bodylife",
                table: "freezes",
                column: "recorded_by_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_freezes_session_id",
                schema: "bodylife",
                table: "freezes",
                column: "session_id");

            migrationBuilder.CreateIndex(
                name: "IX_issued_membership_sale_corrections_client_id",
                schema: "bodylife",
                table: "issued_membership_sale_corrections",
                column: "client_id");

            migrationBuilder.CreateIndex(
                name: "IX_issued_membership_sale_corrections_original_membership_id_c~",
                schema: "bodylife",
                table: "issued_membership_sale_corrections",
                columns: new[] { "original_membership_id", "client_id" });

            migrationBuilder.CreateIndex(
                name: "IX_issued_membership_sale_corrections_original_payment_id_clie~",
                schema: "bodylife",
                table: "issued_membership_sale_corrections",
                columns: new[] { "original_payment_id", "client_id" });

            migrationBuilder.CreateIndex(
                name: "IX_issued_membership_sale_corrections_recorded_by_account_id",
                schema: "bodylife",
                table: "issued_membership_sale_corrections",
                column: "recorded_by_account_id");

            migrationBuilder.CreateIndex(
                name: "IX_issued_membership_sale_corrections_replacement_membership_i~",
                schema: "bodylife",
                table: "issued_membership_sale_corrections",
                columns: new[] { "replacement_membership_id", "client_id" });

            migrationBuilder.CreateIndex(
                name: "IX_issued_membership_sale_corrections_replacement_payment_id_c~",
                schema: "bodylife",
                table: "issued_membership_sale_corrections",
                columns: new[] { "replacement_payment_id", "client_id" });

            migrationBuilder.CreateIndex(
                name: "IX_issued_membership_sale_corrections_session_id",
                schema: "bodylife",
                table: "issued_membership_sale_corrections",
                column: "session_id");

            migrationBuilder.CreateIndex(
                name: "ux_issued_sale_corrections_original_membership",
                schema: "bodylife",
                table: "issued_membership_sale_corrections",
                column: "original_membership_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_issued_sale_corrections_original_payment",
                schema: "bodylife",
                table: "issued_membership_sale_corrections",
                column: "original_payment_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_issued_sale_corrections_replacement_membership",
                schema: "bodylife",
                table: "issued_membership_sale_corrections",
                column: "replacement_membership_id",
                unique: true,
                filter: "replacement_membership_id is not null");

            migrationBuilder.CreateIndex(
                name: "ux_issued_sale_corrections_replacement_payment",
                schema: "bodylife",
                table: "issued_membership_sale_corrections",
                column: "replacement_payment_id",
                unique: true,
                filter: "replacement_payment_id is not null");

            migrationBuilder.CreateIndex(
                name: "ix_issued_memberships_client_timeline",
                schema: "bodylife",
                table: "issued_memberships",
                columns: new[] { "client_id", "start_date", "issued_at" },
                descending: new[] { false, true, true });

            migrationBuilder.CreateIndex(
                name: "ix_issued_memberships_issued_by_account_id",
                schema: "bodylife",
                table: "issued_memberships",
                column: "issued_by_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_issued_memberships_membership_type_id",
                schema: "bodylife",
                table: "issued_memberships",
                column: "membership_type_id");

            migrationBuilder.CreateIndex(
                name: "ix_membership_adjustments_active_membership_effective_date",
                schema: "bodylife",
                table: "membership_adjustments",
                columns: new[] { "membership_id", "effective_date", "adjustment_type" },
                filter: "status = 'active'");

            migrationBuilder.CreateIndex(
                name: "ix_membership_adjustments_membership_timeline",
                schema: "bodylife",
                table: "membership_adjustments",
                columns: new[] { "membership_id", "effective_date", "recorded_at" },
                descending: new[] { false, true, true });

            migrationBuilder.CreateIndex(
                name: "ix_membership_adjustments_recorded_by_account_id",
                schema: "bodylife",
                table: "membership_adjustments",
                column: "recorded_by_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_membership_adjustments_recorded_session_id",
                schema: "bodylife",
                table: "membership_adjustments",
                column: "recorded_session_id");

            migrationBuilder.CreateIndex(
                name: "ix_membership_extension_days_active_membership_date",
                schema: "bodylife",
                table: "membership_extension_days",
                columns: new[] { "membership_id", "extension_date" },
                filter: "is_active");

            migrationBuilder.CreateIndex(
                name: "ix_membership_extension_days_source",
                schema: "bodylife",
                table: "membership_extension_days",
                columns: new[] { "source_type", "source_id" });

            migrationBuilder.CreateIndex(
                name: "ux_membership_extension_days_membership_date_source",
                schema: "bodylife",
                table: "membership_extension_days",
                columns: new[] { "membership_id", "extension_date", "source_type", "source_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_membership_negative_closure_corrections_recorded_by_account~",
                schema: "bodylife",
                table: "membership_negative_closure_corrections",
                column: "recorded_by_account_id");

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

            migrationBuilder.CreateIndex(
                name: "ix_membership_opening_states_membership_timeline",
                schema: "bodylife",
                table: "membership_opening_states",
                columns: new[] { "membership_id", "opening_as_of_date", "recorded_at" },
                descending: new[] { false, true, true });

            migrationBuilder.CreateIndex(
                name: "ix_membership_opening_states_recorded_by_account_id",
                schema: "bodylife",
                table: "membership_opening_states",
                column: "recorded_by_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_membership_opening_states_recorded_session_id",
                schema: "bodylife",
                table: "membership_opening_states",
                column: "recorded_session_id");

            migrationBuilder.CreateIndex(
                name: "ux_membership_opening_states_active_membership",
                schema: "bodylife",
                table: "membership_opening_states",
                column: "membership_id",
                unique: true,
                filter: "status = 'active'");

            migrationBuilder.CreateIndex(
                name: "ux_membership_replacement_dependencies_original",
                schema: "bodylife",
                table: "membership_replacement_dependency_items",
                columns: new[] { "sale_correction_id", "dependency_type", "original_fact_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_membership_state_cache_effective_end_date",
                schema: "bodylife",
                table: "membership_state_cache",
                column: "effective_end_date");

            migrationBuilder.CreateIndex(
                name: "ix_membership_state_cache_last_counted_visit_at",
                schema: "bodylife",
                table: "membership_state_cache",
                column: "last_counted_visit_at");

            migrationBuilder.CreateIndex(
                name: "ix_membership_state_cache_negative_balance_open",
                schema: "bodylife",
                table: "membership_state_cache",
                column: "negative_balance",
                filter: "negative_balance > 0");

            migrationBuilder.CreateIndex(
                name: "ix_membership_state_cache_remaining_visits",
                schema: "bodylife",
                table: "membership_state_cache",
                column: "remaining_visits");

            migrationBuilder.CreateIndex(
                name: "ix_membership_types_active_issue_order",
                schema: "bodylife",
                table: "membership_types",
                columns: new[] { "name", "id" },
                filter: "is_active");

            migrationBuilder.CreateIndex(
                name: "ix_non_working_applications_client_timeline",
                schema: "bodylife",
                table: "non_working_period_applications",
                columns: new[] { "client_id", "confirmed_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_non_working_applications_membership_status_range",
                schema: "bodylife",
                table: "non_working_period_applications",
                columns: new[] { "membership_id", "status", "applied_start_date", "applied_end_date" });

            migrationBuilder.CreateIndex(
                name: "IX_non_working_period_applications_membership_id_client_id",
                schema: "bodylife",
                table: "non_working_period_applications",
                columns: new[] { "membership_id", "client_id" });

            migrationBuilder.CreateIndex(
                name: "IX_non_working_period_applications_non_working_period_id_appli~",
                schema: "bodylife",
                table: "non_working_period_applications",
                columns: new[] { "non_working_period_id", "applied_start_date", "applied_end_date" });

            migrationBuilder.CreateIndex(
                name: "ux_non_working_applications_active_period_membership",
                schema: "bodylife",
                table: "non_working_period_applications",
                columns: new[] { "non_working_period_id", "membership_id" },
                unique: true,
                filter: "status = 'active'");

            migrationBuilder.CreateIndex(
                name: "ix_non_working_period_cancellations_account_id",
                schema: "bodylife",
                table: "non_working_period_cancellations",
                column: "recorded_by_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_non_working_period_cancellations_session_id",
                schema: "bodylife",
                table: "non_working_period_cancellations",
                column: "session_id");

            migrationBuilder.CreateIndex(
                name: "ix_non_working_period_cancellations_timeline",
                schema: "bodylife",
                table: "non_working_period_cancellations",
                columns: new[] { "recorded_at", "non_working_period_id" },
                descending: new[] { true, false });

            migrationBuilder.CreateIndex(
                name: "ux_non_working_period_cancellations_period_id",
                schema: "bodylife",
                table: "non_working_period_cancellations",
                column: "non_working_period_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_non_working_periods_created_by_account_id",
                schema: "bodylife",
                table: "non_working_periods",
                column: "created_by_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_non_working_periods_session_id",
                schema: "bodylife",
                table: "non_working_periods",
                column: "session_id");

            migrationBuilder.CreateIndex(
                name: "ix_non_working_periods_status_range",
                schema: "bodylife",
                table: "non_working_periods",
                columns: new[] { "status", "start_date", "end_date" });

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
                name: "IX_payments_negative_closure_id_client_id",
                schema: "bodylife",
                table: "payments",
                columns: new[] { "negative_closure_id", "client_id" });

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

            migrationBuilder.CreateIndex(
                name: "ux_payments_active_negative_closure",
                schema: "bodylife",
                table: "payments",
                column: "negative_closure_id",
                unique: true,
                filter: "payment_context = 'negative_closure' and status = 'active'");

            migrationBuilder.CreateIndex(
                name: "ux_payments_membership_sale_membership",
                schema: "bodylife",
                table: "payments",
                column: "membership_id",
                unique: true,
                filter: "payment_context = 'membership_sale'");

            migrationBuilder.CreateIndex(
                name: "ix_sessions_active_account_expires_at",
                schema: "bodylife",
                table: "sessions",
                columns: new[] { "account_id", "expires_at" },
                filter: "ended_at is null");

            migrationBuilder.CreateIndex(
                name: "ix_visit_cancellations_recorded_by_account_id",
                schema: "bodylife",
                table: "visit_cancellations",
                column: "recorded_by_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_visit_cancellations_session_id",
                schema: "bodylife",
                table: "visit_cancellations",
                column: "session_id");

            migrationBuilder.CreateIndex(
                name: "ix_visit_cancellations_timeline",
                schema: "bodylife",
                table: "visit_cancellations",
                columns: new[] { "occurred_at", "recorded_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ux_visit_cancellations_visit_id",
                schema: "bodylife",
                table: "visit_cancellations",
                column: "visit_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_visit_consumptions_membership_client",
                schema: "bodylife",
                table: "visit_consumptions",
                columns: new[] { "membership_id", "client_id" });

            migrationBuilder.CreateIndex(
                name: "ix_visit_consumptions_membership_recalculation",
                schema: "bodylife",
                table: "visit_consumptions",
                columns: new[] { "membership_id", "status", "recorded_at", "visit_id" });

            migrationBuilder.CreateIndex(
                name: "ix_visit_consumptions_recorded_by_account_id",
                schema: "bodylife",
                table: "visit_consumptions",
                column: "recorded_by_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_visit_consumptions_recorded_session_id",
                schema: "bodylife",
                table: "visit_consumptions",
                column: "recorded_session_id");

            migrationBuilder.CreateIndex(
                name: "ix_visit_consumptions_visit_client_kind",
                schema: "bodylife",
                table: "visit_consumptions",
                columns: new[] { "visit_id", "client_id", "visit_kind" });

            migrationBuilder.CreateIndex(
                name: "ux_visit_consumptions_active_counted_visit",
                schema: "bodylife",
                table: "visit_consumptions",
                column: "visit_id",
                unique: true,
                filter: "status = 'active' and consumption_type = 'counted'");

            migrationBuilder.CreateIndex(
                name: "ux_visit_consumptions_active_negative_coverage_source",
                schema: "bodylife",
                table: "visit_consumptions",
                column: "source_fact_id",
                unique: true,
                filter: "status = 'active' and consumption_type = 'negative_coverage'");

            migrationBuilder.CreateIndex(
                name: "ix_visits_active_daily_report",
                schema: "bodylife",
                table: "visits",
                columns: new[] { "occurred_at", "client_id" },
                filter: "status = 'active'");

            migrationBuilder.CreateIndex(
                name: "ix_visits_client_timeline",
                schema: "bodylife",
                table: "visits",
                columns: new[] { "client_id", "occurred_at", "recorded_at" },
                descending: new[] { false, true, true });

            migrationBuilder.CreateIndex(
                name: "ix_visits_daily_source",
                schema: "bodylife",
                table: "visits",
                columns: new[] { "occurred_at", "status", "client_id" });

            migrationBuilder.CreateIndex(
                name: "ix_visits_recorded_by_account_id",
                schema: "bodylife",
                table: "visits",
                column: "recorded_by_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_visits_session_id",
                schema: "bodylife",
                table: "visits",
                column: "session_id");

            migrationBuilder.CreateTable(
                name: "membership_lifecycle_closures",
                schema: "bodylife",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_membership_id = table.Column<Guid>(type: "uuid", nullable: false),
                    successor_membership_id = table.Column<Guid>(type: "uuid", nullable: true),
                    negative_closure_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reason_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    recorded_by_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    correlation_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    entry_origin = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    entry_batch_id = table.Column<Guid>(type: "uuid", nullable: true),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    explanation = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_membership_lifecycle_closures", x => x.id);
                    table.CheckConstraint("ck_membership_lifecycle_closures_correlation", "length(btrim(correlation_id)) > 0");
                    table.CheckConstraint("ck_membership_lifecycle_closures_distinct_memberships", "successor_membership_id is null or successor_membership_id <> source_membership_id");
                    table.CheckConstraint("ck_membership_lifecycle_closures_explanation", "explanation is null or length(btrim(explanation)) > 0");
                    table.CheckConstraint("ck_membership_lifecycle_closures_idempotency", "length(btrim(idempotency_key)) > 0");
                    table.CheckConstraint("ck_membership_lifecycle_closures_origin", "entry_origin in ('normal', 'manual_backfill', 'paper_fallback', 'future_import')");
                    table.CheckConstraint("ck_membership_lifecycle_closures_reason", "reason_code in ('zero_balance_rollover', 'negative_balance_rollover', 'one_off_zero_balance')");
                    table.CheckConstraint("ck_membership_lifecycle_closures_shape", "(reason_code in ('zero_balance_rollover', 'negative_balance_rollover') and successor_membership_id is not null and negative_closure_id is null) or (reason_code = 'one_off_zero_balance' and successor_membership_id is null and negative_closure_id is not null)");
                    table.ForeignKey(
                        name: "FK_membership_lifecycle_closures_accounts_recorded_by_account_~",
                        column: x => x.recorded_by_account_id,
                        principalSchema: "bodylife",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_membership_lifecycle_closures_clients_client_id",
                        column: x => x.client_id,
                        principalSchema: "bodylife",
                        principalTable: "clients",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_membership_lifecycle_closures_issued_memberships_source_mem~",
                        columns: x => new { x.source_membership_id, x.client_id },
                        principalSchema: "bodylife",
                        principalTable: "issued_memberships",
                        principalColumns: new[] { "id", "client_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_membership_lifecycle_closures_issued_memberships_successor_~",
                        columns: x => new { x.successor_membership_id, x.client_id },
                        principalSchema: "bodylife",
                        principalTable: "issued_memberships",
                        principalColumns: new[] { "id", "client_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_membership_lifecycle_closures_membership_negative_closures_~",
                        columns: x => new { x.negative_closure_id, x.client_id },
                        principalSchema: "bodylife",
                        principalTable: "membership_negative_closures",
                        principalColumns: new[] { "id", "client_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_membership_lifecycle_closures_sessions_session_id",
                        column: x => x.session_id,
                        principalSchema: "bodylife",
                        principalTable: "sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ux_issued_memberships_active_client",
                schema: "bodylife",
                table: "issued_memberships",
                column: "client_id",
                unique: true,
                filter: "status = 'active'");

            migrationBuilder.CreateIndex(
                name: "ix_membership_lifecycle_closures_client_timeline",
                schema: "bodylife",
                table: "membership_lifecycle_closures",
                columns: new[] { "client_id", "recorded_at" });

            migrationBuilder.CreateIndex(
                name: "IX_membership_lifecycle_closures_negative_closure_id_client_id",
                schema: "bodylife",
                table: "membership_lifecycle_closures",
                columns: new[] { "negative_closure_id", "client_id" });

            migrationBuilder.CreateIndex(
                name: "IX_membership_lifecycle_closures_recorded_by_account_id",
                schema: "bodylife",
                table: "membership_lifecycle_closures",
                column: "recorded_by_account_id");

            migrationBuilder.CreateIndex(
                name: "IX_membership_lifecycle_closures_session_id",
                schema: "bodylife",
                table: "membership_lifecycle_closures",
                column: "session_id");

            migrationBuilder.CreateIndex(
                name: "IX_membership_lifecycle_closures_source_membership_id_client_id",
                schema: "bodylife",
                table: "membership_lifecycle_closures",
                columns: new[] { "source_membership_id", "client_id" });

            migrationBuilder.CreateIndex(
                name: "ix_membership_lifecycle_closures_successor_membership",
                schema: "bodylife",
                table: "membership_lifecycle_closures",
                column: "successor_membership_id");

            migrationBuilder.CreateIndex(
                name: "IX_membership_lifecycle_closures_successor_membership_id_clien~",
                schema: "bodylife",
                table: "membership_lifecycle_closures",
                columns: new[] { "successor_membership_id", "client_id" });

            migrationBuilder.CreateIndex(
                name: "ux_membership_lifecycle_closures_negative_closure",
                schema: "bodylife",
                table: "membership_lifecycle_closures",
                column: "negative_closure_id",
                unique: true,
                filter: "negative_closure_id is not null");

            migrationBuilder.CreateIndex(
                name: "ux_membership_lifecycle_closures_source_membership",
                schema: "bodylife",
                table: "membership_lifecycle_closures",
                column: "source_membership_id",
                unique: true);

            AddPostgreSqlInvariants(migrationBuilder);
            AddMembershipLifecycleInvariants(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            RemoveMembershipLifecycleInvariants(migrationBuilder);
            RemovePostgreSqlInvariants(migrationBuilder);

            migrationBuilder.DropTable(
                name: "account_credentials",
                schema: "bodylife");

            migrationBuilder.DropTable(
                name: "business_audit_entries",
                schema: "bodylife");

            migrationBuilder.DropTable(
                name: "client_card_assignments",
                schema: "bodylife");

            migrationBuilder.DropTable(
                name: "command_idempotency_keys",
                schema: "bodylife");

            migrationBuilder.DropTable(
                name: "duplicate_warning_acknowledgements",
                schema: "bodylife");

            migrationBuilder.DropTable(
                name: "entry_batch_row_entities",
                schema: "bodylife");

            migrationBuilder.DropTable(
                name: "freeze_cancellations",
                schema: "bodylife");

            migrationBuilder.DropTable(
                name: "membership_adjustments",
                schema: "bodylife");

            migrationBuilder.DropTable(
                name: "membership_extension_days",
                schema: "bodylife");

            migrationBuilder.DropTable(
                name: "membership_negative_closure_corrections",
                schema: "bodylife");

            migrationBuilder.DropTable(
                name: "membership_lifecycle_closures",
                schema: "bodylife");

            migrationBuilder.DropTable(
                name: "membership_negative_closure_items",
                schema: "bodylife");

            migrationBuilder.DropTable(
                name: "membership_opening_states",
                schema: "bodylife");

            migrationBuilder.DropTable(
                name: "membership_replacement_dependency_items",
                schema: "bodylife");

            migrationBuilder.DropTable(
                name: "membership_state_cache",
                schema: "bodylife");

            migrationBuilder.DropTable(
                name: "non_working_period_applications",
                schema: "bodylife");

            migrationBuilder.DropTable(
                name: "non_working_period_cancellations",
                schema: "bodylife");

            migrationBuilder.DropTable(
                name: "payment_cancellations",
                schema: "bodylife");

            migrationBuilder.DropTable(
                name: "payment_corrections",
                schema: "bodylife");

            migrationBuilder.DropTable(
                name: "visit_cancellations",
                schema: "bodylife");

            migrationBuilder.DropTable(
                name: "entry_batch_rows",
                schema: "bodylife");

            migrationBuilder.DropTable(
                name: "freezes",
                schema: "bodylife");

            migrationBuilder.DropTable(
                name: "visit_consumptions",
                schema: "bodylife");

            migrationBuilder.DropTable(
                name: "membership_negative_closure_lines",
                schema: "bodylife");

            migrationBuilder.DropTable(
                name: "issued_membership_sale_corrections",
                schema: "bodylife");

            migrationBuilder.DropTable(
                name: "non_working_periods",
                schema: "bodylife");

            migrationBuilder.DropTable(
                name: "entry_batches",
                schema: "bodylife");

            migrationBuilder.DropTable(
                name: "payments",
                schema: "bodylife");

            migrationBuilder.DropTable(
                name: "membership_negative_closures",
                schema: "bodylife");

            migrationBuilder.DropTable(
                name: "issued_memberships",
                schema: "bodylife");

            migrationBuilder.DropTable(
                name: "visits",
                schema: "bodylife");

            migrationBuilder.DropTable(
                name: "membership_types",
                schema: "bodylife");

            migrationBuilder.DropTable(
                name: "clients",
                schema: "bodylife");

            migrationBuilder.DropTable(
                name: "sessions",
                schema: "bodylife");

            migrationBuilder.DropTable(
                name: "accounts",
                schema: "bodylife");
        }
    }
}
