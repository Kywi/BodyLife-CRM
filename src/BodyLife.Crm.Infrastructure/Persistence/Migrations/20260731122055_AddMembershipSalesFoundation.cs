using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BodyLife.Crm.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMembershipSalesFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "kind",
                schema: "bodylife",
                table: "membership_types",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "issuance_mode",
                schema: "bodylife",
                table: "issued_memberships",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.Sql(
                """
                do $bodylife$
                begin
                    if exists (
                        select 1
                        from bodylife.membership_types
                        where is_active
                          and price_amount <= 0
                    ) then
                        raise exception using
                            errcode = '23514',
                            constraint = 'ck_membership_types_active_sale_terms',
                            message = 'ADR-018 migration refused: an active MembershipType has a non-positive price.';
                    end if;

                    update bodylife.membership_types
                    set kind = 'ordinary'
                    where kind is null;

                    if exists (
                        select 1
                        from bodylife.issued_memberships membership
                        left join lateral (
                            select
                                count(*) as sale_count,
                                count(*) filter (
                                    where payment.amount = membership.price_amount_snapshot
                                      and payment.currency = membership.price_currency_snapshot
                                      and payment.method = 'cash'
                                ) as exact_count,
                                count(*) filter (where payment.status = 'active') as active_count
                            from bodylife.payments payment
                            where payment.membership_id = membership.id
                              and payment.payment_context = 'membership_sale'
                        ) sale on true
                        left join lateral (
                            select count(*) as opening_count
                            from bodylife.membership_opening_states opening_state
                            where opening_state.membership_id = membership.id
                              and opening_state.status = 'active'
                        ) opening on true
                        where not (
                            (
                                opening.opening_count = 1
                                and sale.sale_count = 0
                            )
                            or (
                                opening.opening_count = 0
                                and sale.sale_count = 1
                                and sale.exact_count = 1
                                and (
                                    (membership.status = 'active' and sale.active_count = 1)
                                    or (membership.status <> 'active' and sale.active_count = 0)
                                )
                            )
                        )
                    ) then
                        raise exception using
                            errcode = '23514',
                            constraint = 'ck_issued_memberships_sale_payment_preflight',
                            message = 'ADR-018 migration refused: every issued Membership must be an unambiguous exact sale or a paymentless opening state.';
                    end if;

                    update bodylife.issued_memberships membership
                    set issuance_mode = case
                        when exists (
                            select 1
                            from bodylife.membership_opening_states opening_state
                            where opening_state.membership_id = membership.id
                              and opening_state.status = 'active'
                        ) then 'opening_state'
                        else 'sale'
                    end;
                end
                $bodylife$;

                alter table bodylife.membership_types
                    alter column kind set not null,
                    alter column kind set default 'ordinary';
                alter table bodylife.issued_memberships
                    alter column issuance_mode set not null;
                """);

            migrationBuilder.CreateIndex(
                name: "ux_payments_membership_sale_membership",
                schema: "bodylife",
                table: "payments",
                column: "membership_id",
                unique: true,
                filter: "payment_context = 'membership_sale'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_payments_membership_sale_membership",
                schema: "bodylife",
                table: "payments",
                sql: "payment_context <> 'membership_sale' or membership_id is not null");

            migrationBuilder.AddCheckConstraint(
                name: "ck_membership_types_active_sale_terms",
                schema: "bodylife",
                table: "membership_types",
                sql: "not is_active or price_amount > 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_membership_types_kind",
                schema: "bodylife",
                table: "membership_types",
                sql: "kind in ('ordinary', 'one_off')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_membership_types_one_off_visits",
                schema: "bodylife",
                table: "membership_types",
                sql: "kind <> 'one_off' or visits_limit = 1");

            migrationBuilder.AddCheckConstraint(
                name: "ck_issued_memberships_issuance_mode",
                schema: "bodylife",
                table: "issued_memberships",
                sql: "issuance_mode in ('sale', 'opening_state')");

            migrationBuilder.Sql(
                """
                create function bodylife.assert_issued_membership_sale_payment(
                    target_membership_id uuid)
                returns void
                language plpgsql
                as $function$
                declare
                    membership_mode text;
                    membership_status text;
                    snapshot_amount numeric;
                    snapshot_currency text;
                    sale_count bigint;
                    exact_count bigint;
                    active_count bigint;
                    opening_count bigint;
                begin
                    if target_membership_id is null then
                        return;
                    end if;

                    select
                        issuance_mode,
                        status,
                        price_amount_snapshot,
                        price_currency_snapshot
                    into
                        membership_mode,
                        membership_status,
                        snapshot_amount,
                        snapshot_currency
                    from bodylife.issued_memberships
                    where id = target_membership_id;

                    if not found then
                        return;
                    end if;

                    select
                        count(*),
                        count(*) filter (
                            where amount = snapshot_amount
                              and currency = snapshot_currency
                              and method = 'cash'
                        ),
                        count(*) filter (where status = 'active')
                    into sale_count, exact_count, active_count
                    from bodylife.payments
                    where membership_id = target_membership_id
                      and payment_context = 'membership_sale';

                    select count(*)
                    into opening_count
                    from bodylife.membership_opening_states
                    where membership_id = target_membership_id
                      and status = 'active';

                    if membership_mode = 'opening_state' then
                        if sale_count <> 0 or opening_count <> 1 then
                            raise exception using
                                errcode = '23514',
                                constraint = 'ck_issued_memberships_opening_state_source',
                                message = 'Opening-state Membership requires one active opening source and no sale Payment.';
                        end if;
                        return;
                    end if;

                    if opening_count <> 0
                        or sale_count <> 1
                        or exact_count <> 1 then
                        raise exception using
                            errcode = '23514',
                            constraint = 'ck_issued_memberships_exact_sale_payment',
                            message = 'Sale Membership requires exactly one cash Payment equal to its price snapshot.';
                    end if;

                    if (membership_status = 'active' and active_count <> 1)
                        or (membership_status <> 'active' and active_count <> 0) then
                        raise exception using
                            errcode = '23514',
                            constraint = 'ck_issued_memberships_sale_payment_status',
                            message = 'Sale Membership and sale Payment lifecycle statuses do not match.';
                    end if;
                end
                $function$;

                create function bodylife.enforce_issued_membership_sale_payment()
                returns trigger
                language plpgsql
                as $function$
                begin
                    if tg_table_name = 'issued_memberships' then
                        perform bodylife.assert_issued_membership_sale_payment(
                            case when tg_op = 'DELETE' then old.id else new.id end);
                    elsif tg_table_name = 'payments' then
                        if tg_op <> 'INSERT'
                            and old.payment_context = 'membership_sale' then
                            perform bodylife.assert_issued_membership_sale_payment(old.membership_id);
                        end if;
                        if tg_op <> 'DELETE'
                            and new.payment_context = 'membership_sale' then
                            perform bodylife.assert_issued_membership_sale_payment(new.membership_id);
                        end if;
                    else
                        if tg_op <> 'INSERT' then
                            perform bodylife.assert_issued_membership_sale_payment(old.membership_id);
                        end if;
                        if tg_op <> 'DELETE' then
                            perform bodylife.assert_issued_membership_sale_payment(new.membership_id);
                        end if;
                    end if;
                    return null;
                end
                $function$;

                create constraint trigger ck_issued_memberships_exact_sale_payment
                    after insert or update or delete on bodylife.issued_memberships
                    deferrable initially deferred
                    for each row execute function bodylife.enforce_issued_membership_sale_payment();

                create constraint trigger ck_payments_exact_membership_sale
                    after insert or update or delete on bodylife.payments
                    deferrable initially deferred
                    for each row execute function bodylife.enforce_issued_membership_sale_payment();

                create constraint trigger ck_membership_opening_states_issuance_mode
                    after insert or update or delete on bodylife.membership_opening_states
                    deferrable initially deferred
                    for each row execute function bodylife.enforce_issued_membership_sale_payment();

                create function bodylife.prevent_membership_type_kind_change()
                returns trigger
                language plpgsql
                as $function$
                begin
                    if new.kind is distinct from old.kind then
                        raise exception using
                            errcode = '23514',
                            constraint = 'ck_membership_types_kind_immutable',
                            message = 'MembershipType kind is immutable after creation.';
                    end if;
                    return new;
                end
                $function$;

                create trigger ck_membership_types_kind_immutable
                    before update of kind on bodylife.membership_types
                    for each row execute function bodylife.prevent_membership_type_kind_change();

                create function bodylife.prevent_issued_membership_mode_change()
                returns trigger
                language plpgsql
                as $function$
                begin
                    if new.issuance_mode is distinct from old.issuance_mode then
                        raise exception using
                            errcode = '23514',
                            constraint = 'ck_issued_memberships_issuance_mode_immutable',
                            message = 'Issued Membership issuance mode is immutable after creation.';
                    end if;
                    return new;
                end
                $function$;

                create trigger ck_issued_memberships_issuance_mode_immutable
                    before update of issuance_mode on bodylife.issued_memberships
                    for each row execute function bodylife.prevent_issued_membership_mode_change();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                drop trigger if exists ck_issued_memberships_issuance_mode_immutable
                    on bodylife.issued_memberships;
                drop function if exists bodylife.prevent_issued_membership_mode_change();
                drop trigger if exists ck_membership_types_kind_immutable
                    on bodylife.membership_types;
                drop function if exists bodylife.prevent_membership_type_kind_change();
                drop trigger if exists ck_payments_exact_membership_sale
                    on bodylife.payments;
                drop trigger if exists ck_membership_opening_states_issuance_mode
                    on bodylife.membership_opening_states;
                drop trigger if exists ck_issued_memberships_exact_sale_payment
                    on bodylife.issued_memberships;
                drop function if exists bodylife.enforce_issued_membership_sale_payment();
                drop function if exists bodylife.assert_issued_membership_sale_payment(uuid);
                """);

            migrationBuilder.DropIndex(
                name: "ux_payments_membership_sale_membership",
                schema: "bodylife",
                table: "payments");

            migrationBuilder.DropCheckConstraint(
                name: "ck_payments_membership_sale_membership",
                schema: "bodylife",
                table: "payments");

            migrationBuilder.DropCheckConstraint(
                name: "ck_membership_types_active_sale_terms",
                schema: "bodylife",
                table: "membership_types");

            migrationBuilder.DropCheckConstraint(
                name: "ck_membership_types_kind",
                schema: "bodylife",
                table: "membership_types");

            migrationBuilder.DropCheckConstraint(
                name: "ck_membership_types_one_off_visits",
                schema: "bodylife",
                table: "membership_types");

            migrationBuilder.DropCheckConstraint(
                name: "ck_issued_memberships_issuance_mode",
                schema: "bodylife",
                table: "issued_memberships");

            migrationBuilder.DropColumn(
                name: "kind",
                schema: "bodylife",
                table: "membership_types");

            migrationBuilder.DropColumn(
                name: "issuance_mode",
                schema: "bodylife",
                table: "issued_memberships");
        }
    }
}
