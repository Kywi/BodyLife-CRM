using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BodyLife.Crm.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddIssuedMembershipSaleCorrections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    dependency_token = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_issued_membership_sale_corrections", x => x.id);
                    table.CheckConstraint("ck_issued_membership_sale_corrections_distinct_memberships", "replacement_membership_id is null or replacement_membership_id <> original_membership_id");
                    table.CheckConstraint("ck_issued_membership_sale_corrections_distinct_payments", "replacement_payment_id is null or replacement_payment_id <> original_payment_id");
                    table.CheckConstraint("ck_issued_membership_sale_corrections_mode", "correction_mode in ('cancel', 'replace')");
                    table.CheckConstraint("ck_issued_membership_sale_corrections_origin", "entry_origin in ('normal', 'manual_backfill', 'paper_fallback', 'future_import')");
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
                name: "ux_membership_replacement_dependencies_original",
                schema: "bodylife",
                table: "membership_replacement_dependency_items",
                columns: new[] { "sale_correction_id", "dependency_type", "original_fact_id" },
                unique: true);

            migrationBuilder.Sql(
                """
                create function bodylife.assert_issued_membership_sale_correction(
                    target_membership_id uuid)
                returns void
                language plpgsql
                as $function$
                declare
                    membership_client_id uuid;
                    membership_mode text;
                    membership_status text;
                    sale_payment_id uuid;
                    sale_payment_status text;
                    correction_id uuid;
                    correction_mode text;
                    correction_original_payment_id uuid;
                    correction_replacement_membership_id uuid;
                    correction_replacement_payment_id uuid;
                    replacement_membership_client_id uuid;
                    replacement_membership_mode text;
                    replacement_membership_status text;
                    replacement_payment_client_id uuid;
                    replacement_payment_membership_id uuid;
                    replacement_payment_context text;
                    replacement_payment_status text;
                    replacement_has_own_correction boolean;
                begin
                    if target_membership_id is null then
                        return;
                    end if;

                    select membership.client_id,
                           membership.issuance_mode,
                           membership.status
                    into membership_client_id, membership_mode, membership_status
                    from bodylife.issued_memberships membership
                    where membership.id = target_membership_id;

                    if not found then
                        return;
                    end if;

                    select payment.id, payment.status
                    into sale_payment_id, sale_payment_status
                    from bodylife.payments payment
                    where payment.membership_id = target_membership_id
                      and payment.payment_context = 'membership_sale';

                    select
                        correction.id,
                        correction.correction_mode,
                        correction.original_payment_id,
                        correction.replacement_membership_id,
                        correction.replacement_payment_id
                    into
                        correction_id,
                        correction_mode,
                        correction_original_payment_id,
                        correction_replacement_membership_id,
                        correction_replacement_payment_id
                    from bodylife.issued_membership_sale_corrections correction
                    where correction.original_membership_id = target_membership_id;

                    if membership_mode <> 'sale' then
                        if correction_id is not null then
                            raise exception using
                                errcode = '23514',
                                constraint = 'ck_issued_sale_corrections_sale_mode',
                                message = 'Only sale-mode Memberships can have an issued-sale correction.';
                        end if;
                        return;
                    end if;

                    if membership_status = 'active' then
                        if correction_id is not null then
                            raise exception using
                                errcode = '23514',
                                constraint = 'ck_issued_sale_corrections_active_absent',
                                message = 'An active sale Membership cannot have an issued-sale correction.';
                        end if;
                        return;
                    end if;

                    if sale_payment_id is null
                        or correction_id is null
                        or correction_original_payment_id <> sale_payment_id then
                        raise exception using
                            errcode = '23514',
                            constraint = 'ck_issued_sale_corrections_original_source',
                            message = 'A non-active sale Membership requires one correction linked to its exact sale Payment.';
                    end if;

                    if exists (
                        select 1
                        from bodylife.membership_replacement_dependency_items item
                        where item.sale_correction_id = correction_id
                    ) then
                        raise exception using
                            errcode = '23514',
                            constraint = 'ck_issued_sale_corrections_dependencies_unsupported',
                            message = 'Issued-sale dependency transfer is not supported; dependencies must be resolved first.';
                    end if;

                    if membership_status = 'canceled' then
                        if correction_mode <> 'cancel'
                            or sale_payment_status <> 'canceled'
                            or correction_replacement_membership_id is not null
                            or correction_replacement_payment_id is not null then
                            raise exception using
                                errcode = '23514',
                                constraint = 'ck_issued_sale_corrections_cancel_lifecycle',
                                message = 'Canceled sale Membership and Payment require a cancel-only correction.';
                        end if;
                        return;
                    end if;

                    if membership_status <> 'corrected'
                        or correction_mode <> 'replace'
                        or sale_payment_status <> 'replaced'
                        or correction_replacement_membership_id is null
                        or correction_replacement_payment_id is null then
                        raise exception using
                            errcode = '23514',
                            constraint = 'ck_issued_sale_corrections_replace_lifecycle',
                            message = 'Corrected sale Membership and replaced Payment require a complete replacement correction.';
                    end if;

                    select membership.client_id,
                           membership.issuance_mode,
                           membership.status
                    into
                        replacement_membership_client_id,
                        replacement_membership_mode,
                        replacement_membership_status
                    from bodylife.issued_memberships membership
                    where membership.id = correction_replacement_membership_id;

                    select payment.client_id,
                           payment.membership_id,
                           payment.payment_context,
                           payment.status
                    into
                        replacement_payment_client_id,
                        replacement_payment_membership_id,
                        replacement_payment_context,
                        replacement_payment_status
                    from bodylife.payments payment
                    where payment.id = correction_replacement_payment_id;

                    if replacement_membership_client_id <> membership_client_id
                        or replacement_membership_mode <> 'sale'
                        or replacement_payment_client_id <> membership_client_id
                        or replacement_payment_membership_id
                            <> correction_replacement_membership_id
                        or replacement_payment_context <> 'membership_sale' then
                        raise exception using
                            errcode = '23514',
                            constraint = 'ck_issued_sale_corrections_replacement_source',
                            message = 'Replacement Membership and Payment must be one exact sale for the same Client.';
                    end if;

                    if exists (
                        with recursive replacement_chain(membership_id) as (
                            select correction.replacement_membership_id
                            from bodylife.issued_membership_sale_corrections correction
                            where correction.original_membership_id = target_membership_id
                              and correction.correction_mode = 'replace'
                            union
                            select next_correction.replacement_membership_id
                            from replacement_chain chain
                            join bodylife.issued_membership_sale_corrections next_correction
                              on next_correction.original_membership_id = chain.membership_id
                            where next_correction.correction_mode = 'replace'
                        )
                        select 1
                        from replacement_chain
                        where membership_id = target_membership_id
                    ) then
                        raise exception using
                            errcode = '23514',
                            constraint = 'ck_issued_sale_corrections_replacement_cycle',
                            message = 'Issued-sale replacement chains cannot contain a cycle.';
                    end if;

                    select exists (
                        select 1
                        from bodylife.issued_membership_sale_corrections next_correction
                        where next_correction.original_membership_id
                            = correction_replacement_membership_id
                          and next_correction.original_payment_id
                            = correction_replacement_payment_id
                    )
                    into replacement_has_own_correction;

                    if replacement_membership_status = 'active'
                        and replacement_payment_status = 'active'
                        and not replacement_has_own_correction then
                        return;
                    end if;

                    if replacement_has_own_correction
                        and (
                            replacement_membership_status = 'corrected'
                                and replacement_payment_status = 'replaced'
                            or replacement_membership_status = 'canceled'
                                and replacement_payment_status = 'canceled'
                        ) then
                        return;
                    end if;

                    raise exception using
                        errcode = '23514',
                        constraint = 'ck_issued_sale_corrections_replacement_lifecycle',
                        message = 'Replacement sale lifecycle must be active or explained by its own correction.';
                end
                $function$;

                create function bodylife.enforce_issued_membership_sale_correction()
                returns trigger
                language plpgsql
                as $function$
                declare
                    membership_id uuid;
                begin
                    if tg_table_name = 'issued_memberships' then
                        perform bodylife.assert_issued_membership_sale_correction(
                            case when tg_op = 'DELETE' then old.id else new.id end);
                    elsif tg_table_name = 'payments' then
                        if tg_op <> 'INSERT'
                            and old.payment_context = 'membership_sale' then
                            perform bodylife.assert_issued_membership_sale_correction(
                                old.membership_id);
                        end if;
                        if tg_op <> 'DELETE'
                            and new.payment_context = 'membership_sale' then
                            perform bodylife.assert_issued_membership_sale_correction(
                                new.membership_id);
                        end if;
                    elsif tg_table_name = 'issued_membership_sale_corrections' then
                        if tg_op <> 'INSERT' then
                            perform bodylife.assert_issued_membership_sale_correction(
                                old.original_membership_id);
                        end if;
                        if tg_op <> 'DELETE' then
                            perform bodylife.assert_issued_membership_sale_correction(
                                new.original_membership_id);
                        end if;
                    else
                        select correction.original_membership_id
                        into membership_id
                        from bodylife.issued_membership_sale_corrections correction
                        where correction.id = case
                            when tg_op = 'DELETE' then old.sale_correction_id
                            else new.sale_correction_id
                        end;
                        perform bodylife.assert_issued_membership_sale_correction(
                            membership_id);
                    end if;
                    return null;
                end
                $function$;

                create constraint trigger ck_issued_memberships_sale_correction
                    after insert or update or delete on bodylife.issued_memberships
                    deferrable initially deferred
                    for each row execute function bodylife.enforce_issued_membership_sale_correction();

                create constraint trigger ck_payments_sale_correction
                    after insert or update or delete on bodylife.payments
                    deferrable initially deferred
                    for each row execute function bodylife.enforce_issued_membership_sale_correction();

                create constraint trigger ck_issued_sale_corrections_lifecycle
                    after insert or update or delete on bodylife.issued_membership_sale_corrections
                    deferrable initially deferred
                    for each row execute function bodylife.enforce_issued_membership_sale_correction();

                create constraint trigger ck_membership_replacement_dependencies_supported
                    after insert or update or delete on bodylife.membership_replacement_dependency_items
                    deferrable initially deferred
                    for each row execute function bodylife.enforce_issued_membership_sale_correction();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                drop trigger if exists ck_membership_replacement_dependencies_supported
                    on bodylife.membership_replacement_dependency_items;
                drop trigger if exists ck_issued_sale_corrections_lifecycle
                    on bodylife.issued_membership_sale_corrections;
                drop trigger if exists ck_payments_sale_correction
                    on bodylife.payments;
                drop trigger if exists ck_issued_memberships_sale_correction
                    on bodylife.issued_memberships;
                drop function if exists bodylife.enforce_issued_membership_sale_correction();
                drop function if exists bodylife.assert_issued_membership_sale_correction(uuid);
                """);

            migrationBuilder.DropTable(
                name: "membership_replacement_dependency_items",
                schema: "bodylife");

            migrationBuilder.DropTable(
                name: "issued_membership_sale_corrections",
                schema: "bodylife");
        }
    }
}
