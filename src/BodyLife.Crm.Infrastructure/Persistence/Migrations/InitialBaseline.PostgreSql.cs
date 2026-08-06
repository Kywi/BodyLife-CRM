using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BodyLife.Crm.Infrastructure.Persistence.Migrations
{
    public partial class InitialBaseline
    {
        private static void AddPostgreSqlInvariants(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                create function bodylife.reject_business_audit_entry_mutation()
                returns trigger
                language plpgsql
                as $bodylife$
                begin
                    raise exception using
                        errcode = 'P0001',
                        message = format(
                            'bodylife.business_audit_entries is append-only; %s is not allowed',
                            tg_op);
                    return null;
                end;
                $bodylife$;

                create trigger trg_business_audit_entries_append_only
                before update or delete on bodylife.business_audit_entries
                for each statement
                execute function bodylife.reject_business_audit_entry_mutation();
                """);

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

                """);

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

            AddPaperFallbackLinkInvariants(migrationBuilder);
        }

        private static void RemovePostgreSqlInvariants(MigrationBuilder migrationBuilder)
        {
            RemovePaperFallbackLinkInvariants(migrationBuilder);

            migrationBuilder.Sql(
                """
                drop trigger if exists ck_entry_batch_rows_parent on bodylife.entry_batch_rows;
                drop trigger if exists ck_entry_batches_immutable on bodylife.entry_batches;
                drop function bodylife.enforce_entry_batch_row_parent();
                drop function bodylife.enforce_entry_batch_immutable_fields();
                """);

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
                """);

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

            migrationBuilder.Sql(
                """
                drop trigger trg_business_audit_entries_append_only
                    on bodylife.business_audit_entries;
                drop function bodylife.reject_business_audit_entry_mutation();
                """);
        }
    }
}
