using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BodyLife.Crm.Infrastructure.Persistence.Migrations
{
    public partial class InitialBaseline
    {
        private static void AddPaperFallbackLinkInvariants(
            MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                create function bodylife.paper_entity_source_batch(
                    target_entity_type text,
                    target_entity_id uuid)
                returns uuid
                language plpgsql
                stable
                as $function$
                declare
                    source_origin text;
                    source_batch_id uuid;
                    source_found boolean;
                begin
                    case target_entity_type
                        when 'visit' then
                            select source.entry_origin, source.entry_batch_id
                            into source_origin, source_batch_id
                            from bodylife.visits source
                            where source.id = target_entity_id;
                        when 'visit_cancellation' then
                            select source.entry_origin, source.entry_batch_id
                            into source_origin, source_batch_id
                            from bodylife.visit_cancellations source
                            where source.id = target_entity_id;
                        when 'payment' then
                            select source.entry_origin, source.entry_batch_id
                            into source_origin, source_batch_id
                            from bodylife.payments source
                            where source.id = target_entity_id;
                        when 'payment_correction' then
                            select source.entry_origin, source.entry_batch_id
                            into source_origin, source_batch_id
                            from bodylife.payment_corrections source
                            where source.id = target_entity_id;
                        when 'payment_cancellation' then
                            select source.entry_origin, source.entry_batch_id
                            into source_origin, source_batch_id
                            from bodylife.payment_cancellations source
                            where source.id = target_entity_id;
                        when 'freeze' then
                            select source.entry_origin, source.entry_batch_id
                            into source_origin, source_batch_id
                            from bodylife.freezes source
                            where source.id = target_entity_id;
                        when 'freeze_cancellation' then
                            select source.entry_origin, source.entry_batch_id
                            into source_origin, source_batch_id
                            from bodylife.freeze_cancellations source
                            where source.id = target_entity_id;
                        when 'membership' then
                            select source.entry_origin, source.entry_batch_id
                            into source_origin, source_batch_id
                            from bodylife.issued_memberships source
                            where source.id = target_entity_id;
                        when 'issued_membership_sale_correction' then
                            select source.entry_origin, source.entry_batch_id
                            into source_origin, source_batch_id
                            from bodylife.issued_membership_sale_corrections source
                            where source.id = target_entity_id;
                        when 'membership_negative_closure' then
                            select source.entry_origin, source.entry_batch_id
                            into source_origin, source_batch_id
                            from bodylife.membership_negative_closures source
                            where source.id = target_entity_id;
                        when 'membership_lifecycle_closure' then
                            select source.entry_origin, source.entry_batch_id
                            into source_origin, source_batch_id
                            from bodylife.membership_lifecycle_closures source
                            where source.id = target_entity_id;
                        when 'membership_negative_closure_correction' then
                            select source.entry_origin, source.entry_batch_id
                            into source_origin, source_batch_id
                            from bodylife.membership_negative_closure_corrections source
                            where source.id = target_entity_id;
                        when 'membership_negative_closure_line' then
                            select closure.entry_origin, closure.entry_batch_id
                            into source_origin, source_batch_id
                            from bodylife.membership_negative_closure_lines source
                            join bodylife.membership_negative_closures closure
                              on closure.id = source.negative_closure_id
                            where source.id = target_entity_id;
                        when 'membership_negative_closure_item' then
                            select closure.entry_origin, closure.entry_batch_id
                            into source_origin, source_batch_id
                            from bodylife.membership_negative_closure_items source
                            join bodylife.membership_negative_closures closure
                              on closure.id = source.negative_closure_id
                            where source.id = target_entity_id;
                        when 'visit_consumption' then
                            select closure.entry_origin, closure.entry_batch_id
                            into source_origin, source_batch_id
                            from bodylife.membership_negative_closure_items item
                            join bodylife.membership_negative_closures closure
                              on closure.id = item.negative_closure_id
                            where item.new_consumption_id = target_entity_id;
                        else
                            raise exception using
                                errcode = '23514',
                                constraint = 'ck_entry_batch_row_entities_known_type',
                                message = format(
                                    'Unsupported paper fallback entity type: %s.',
                                    target_entity_type);
                    end case;

                    source_found := found;
                    if not source_found then
                        return null;
                    end if;

                    if source_origin = 'paper_fallback' then
                        if source_batch_id is null then
                            raise exception using
                                errcode = '23514',
                                constraint = 'ck_paper_source_requires_batch',
                                message = 'A paper fallback source fact requires an entry batch.';
                        end if;

                        return source_batch_id;
                    end if;

                    return null;
                end
                $function$;

                create function bodylife.paper_row_matches_expected_links(
                    target_row_id uuid,
                    expected_entity_types text[],
                    expected_entity_ids uuid[])
                returns boolean
                language sql
                stable
                as $function$
                    with expected(entity_type, entity_id) as (
                        select entity_type, entity_id
                        from unnest(expected_entity_types, expected_entity_ids)
                            as expected_link(entity_type, entity_id)
                    ), difference as (
                        (
                            select actual.entity_type, actual.entity_id
                            from bodylife.entry_batch_row_entities actual
                            where actual.entry_batch_row_id = target_row_id
                            except
                            select expected.entity_type, expected.entity_id
                            from expected
                        )
                        union all
                        (
                            select expected.entity_type, expected.entity_id
                            from expected
                            except
                            select actual.entity_type, actual.entity_id
                            from bodylife.entry_batch_row_entities actual
                            where actual.entry_batch_row_id = target_row_id
                        )
                    )
                    select expected_entity_types is not null
                       and expected_entity_ids is not null
                       and not exists (select 1 from difference);
                $function$;

                create function bodylife.assert_paper_entity_link(
                    target_entity_type text,
                    target_entity_id uuid)
                returns void
                language plpgsql
                as $function$
                declare
                    source_batch_id uuid;
                    linked_batch_id uuid;
                    link_count bigint;
                begin
                    source_batch_id := bodylife.paper_entity_source_batch(
                        target_entity_type,
                        target_entity_id);

                    select count(*), min(row.entry_batch_id::text)::uuid
                    into link_count, linked_batch_id
                    from bodylife.entry_batch_row_entities link
                    join bodylife.entry_batch_rows row
                      on row.id = link.entry_batch_row_id
                    where link.entity_type = target_entity_type
                      and link.entity_id = target_entity_id;

                    if source_batch_id is null then
                        if link_count <> 0 then
                            raise exception using
                                errcode = '23514',
                                constraint = 'ck_entry_batch_row_entities_paper_source',
                                message = 'A paper row link must reference an existing paper fallback source fact.';
                        end if;
                        return;
                    end if;

                    if link_count <> 1
                        or linked_batch_id is distinct from source_batch_id then
                        raise exception using
                            errcode = '23514',
                            constraint = 'ck_paper_source_exact_row_link',
                            message = 'A paper fallback source fact requires exactly one row link in its source batch.';
                    end if;
                end
                $function$;

                create function bodylife.assert_entry_batch_row_entity_links(
                    target_row_id uuid)
                returns void
                language plpgsql
                as $function$
                declare
                    target_event_type text;
                    target_batch_id uuid;
                    target_batch_type text;
                    link_count bigint;
                    anchor_count bigint;
                    anchor_type text;
                    anchor_id uuid;
                    expected_types text[];
                    expected_ids uuid[];
                    linked record;
                begin
                    select row.event_type, row.entry_batch_id, batch.batch_type
                    into target_event_type, target_batch_id, target_batch_type
                    from bodylife.entry_batch_rows row
                    join bodylife.entry_batches batch
                      on batch.id = row.entry_batch_id
                    where row.id = target_row_id;

                    if not found then
                        return;
                    end if;

                    select count(*)
                    into link_count
                    from bodylife.entry_batch_row_entities link
                    where link.entry_batch_row_id = target_row_id;

                    if link_count = 0 then
                        return;
                    end if;

                    if target_batch_type <> 'paper_fallback' then
                        raise exception using
                            errcode = '23514',
                            constraint = 'ck_entry_batch_row_entities_paper_batch',
                            message = 'Canonical entity links are only valid for a paper fallback batch.';
                    end if;

                    for linked in
                        select link.entity_type, link.entity_id
                        from bodylife.entry_batch_row_entities link
                        where link.entry_batch_row_id = target_row_id
                    loop
                        if bodylife.paper_entity_source_batch(
                            linked.entity_type,
                            linked.entity_id) is distinct from target_batch_id then
                            raise exception using
                                errcode = '23514',
                                constraint = 'ck_entry_batch_row_entities_source_batch',
                                message = 'Every paper row link must reference a source fact from the same batch.';
                        end if;
                    end loop;

                    select
                        count(*),
                        min(link.entity_type),
                        min(link.entity_id::text)::uuid
                    into anchor_count, anchor_type, anchor_id
                    from bodylife.entry_batch_row_entities link
                    where link.entry_batch_row_id = target_row_id
                      and (
                        (target_event_type = 'visit'
                            and link.entity_type = 'visit')
                        or (target_event_type = 'payment'
                            and link.entity_type = 'payment')
                        or (target_event_type = 'freeze'
                            and link.entity_type = 'freeze')
                        or (target_event_type = 'membership_sale'
                            and link.entity_type = 'membership')
                        or (target_event_type = 'negative_coverage'
                            and link.entity_type = 'membership_negative_closure')
                        or (target_event_type = 'correction_or_cancellation'
                            and link.entity_type in (
                                'visit_cancellation',
                                'payment_correction',
                                'payment_cancellation',
                                'freeze_cancellation',
                                'issued_membership_sale_correction',
                                'membership_negative_closure_correction'))
                      );

                    if anchor_count <> 1 then
                        raise exception using
                            errcode = '23514',
                            constraint = 'ck_entry_batch_row_entities_anchor',
                            message = 'A linked paper row requires exactly one canonical event anchor.';
                    end if;

                    if target_event_type = 'visit' then
                        expected_types := array['visit'];
                        expected_ids := array[anchor_id];
                    elsif target_event_type = 'payment' then
                        if not exists (
                            select 1
                            from bodylife.payments payment
                            where payment.id = anchor_id
                              and payment.payment_context in (
                                'one_off', 'trial', 'other')) then
                            raise exception using
                                errcode = '23514',
                                constraint = 'ck_entry_batch_row_entities_payment_event',
                                message = 'A payment row must reference one standalone Payment.';
                        end if;
                        expected_types := array['payment'];
                        expected_ids := array[anchor_id];
                    elsif target_event_type = 'freeze' then
                        expected_types := array['freeze'];
                        expected_ids := array[anchor_id];
                    elsif target_event_type = 'membership_sale' then
                        if not exists (
                            select 1
                            from bodylife.issued_memberships membership
                            where membership.id = anchor_id
                              and membership.issuance_mode = 'sale') then
                            raise exception using
                                errcode = '23514',
                                constraint = 'ck_entry_batch_row_entities_membership_sale',
                                message = 'A membership sale row must reference one sale-mode Membership.';
                        end if;

                        select
                            array_agg(expected.entity_type order by expected.entity_type, expected.entity_id),
                            array_agg(expected.entity_id order by expected.entity_type, expected.entity_id)
                        into expected_types, expected_ids
                        from (
                            select 'membership'::text as entity_type, anchor_id as entity_id
                            union all
                            select 'payment', payment.id
                            from bodylife.payments payment
                            where payment.membership_id = anchor_id
                              and payment.payment_context = 'membership_sale'
                            union all
                            select 'membership_negative_closure', closure.id
                            from bodylife.membership_negative_closures closure
                            where closure.covering_membership_id = anchor_id
                              and not exists (
                                select 1
                                from bodylife.membership_negative_closure_corrections correction
                                where correction.replacement_closure_id = closure.id)
                            union all
                            select 'membership_lifecycle_closure', closure.id
                            from bodylife.membership_lifecycle_closures closure
                            where closure.successor_membership_id = anchor_id
                            union all
                            select 'membership_negative_closure_line', line.id
                            from bodylife.membership_negative_closure_lines line
                            join bodylife.membership_negative_closures closure
                              on closure.id = line.negative_closure_id
                            where closure.covering_membership_id = anchor_id
                              and not exists (
                                select 1
                                from bodylife.membership_negative_closure_corrections correction
                                where correction.replacement_closure_id = closure.id)
                            union all
                            select 'membership_negative_closure_item', item.id
                            from bodylife.membership_negative_closure_items item
                            join bodylife.membership_negative_closures closure
                              on closure.id = item.negative_closure_id
                            where closure.covering_membership_id = anchor_id
                              and not exists (
                                select 1
                                from bodylife.membership_negative_closure_corrections correction
                                where correction.replacement_closure_id = closure.id)
                            union all
                            select 'visit_consumption', item.new_consumption_id
                            from bodylife.membership_negative_closure_items item
                            join bodylife.membership_negative_closures closure
                              on closure.id = item.negative_closure_id
                            where closure.covering_membership_id = anchor_id
                              and item.new_consumption_id is not null
                              and not exists (
                                select 1
                                from bodylife.membership_negative_closure_corrections correction
                                where correction.replacement_closure_id = closure.id)
                            union all
                            select 'payment', payment.id
                            from bodylife.payments payment
                            join bodylife.membership_negative_closures closure
                              on closure.id = payment.negative_closure_id
                            where closure.covering_membership_id = anchor_id
                              and not exists (
                                select 1
                                from bodylife.membership_negative_closure_corrections correction
                                where correction.replacement_closure_id = closure.id)
                        ) expected;
                    elsif target_event_type = 'negative_coverage' then
                        if not exists (
                            select 1
                            from bodylife.membership_negative_closures closure
                            where closure.id = anchor_id
                              and closure.closure_type = 'one_off'
                              and not exists (
                                select 1
                                from bodylife.membership_negative_closure_corrections correction
                                where correction.replacement_closure_id = closure.id)) then
                            raise exception using
                                errcode = '23514',
                                constraint = 'ck_entry_batch_row_entities_negative_coverage',
                                message = 'A negative coverage row must reference one initial one-off closure.';
                        end if;

                        select
                            array_agg(expected.entity_type order by expected.entity_type, expected.entity_id),
                            array_agg(expected.entity_id order by expected.entity_type, expected.entity_id)
                        into expected_types, expected_ids
                        from (
                            select 'membership_negative_closure'::text as entity_type, anchor_id as entity_id
                            union all
                            select 'membership_negative_closure_line', line.id
                            from bodylife.membership_negative_closure_lines line
                            where line.negative_closure_id = anchor_id
                            union all
                            select 'membership_negative_closure_item', item.id
                            from bodylife.membership_negative_closure_items item
                            where item.negative_closure_id = anchor_id
                            union all
                            select 'visit_consumption', item.new_consumption_id
                            from bodylife.membership_negative_closure_items item
                            where item.negative_closure_id = anchor_id
                              and item.new_consumption_id is not null
                            union all
                            select 'payment', payment.id
                            from bodylife.payments payment
                            where payment.negative_closure_id = anchor_id
                            union all
                            select 'membership_lifecycle_closure', closure.id
                            from bodylife.membership_lifecycle_closures closure
                            where closure.negative_closure_id = anchor_id
                        ) expected;
                    elsif anchor_type in (
                        'visit_cancellation',
                        'payment_cancellation',
                        'freeze_cancellation') then
                        expected_types := array[anchor_type];
                        expected_ids := array[anchor_id];
                    elsif anchor_type = 'payment_correction' then
                        select
                            array_agg(expected.entity_type order by expected.entity_type, expected.entity_id),
                            array_agg(expected.entity_id order by expected.entity_type, expected.entity_id)
                        into expected_types, expected_ids
                        from (
                            select 'payment_correction'::text as entity_type, anchor_id as entity_id
                            union all
                            select 'payment', correction.replacement_payment_id
                            from bodylife.payment_corrections correction
                            where correction.id = anchor_id
                        ) expected;
                    elsif anchor_type = 'issued_membership_sale_correction' then
                        select
                            array_agg(expected.entity_type order by expected.entity_type, expected.entity_id),
                            array_agg(expected.entity_id order by expected.entity_type, expected.entity_id)
                        into expected_types, expected_ids
                        from (
                            select 'issued_membership_sale_correction'::text as entity_type, anchor_id as entity_id
                            union all
                            select 'membership', correction.replacement_membership_id
                            from bodylife.issued_membership_sale_corrections correction
                            where correction.id = anchor_id
                              and correction.correction_mode = 'replace'
                            union all
                            select 'payment', correction.replacement_payment_id
                            from bodylife.issued_membership_sale_corrections correction
                            where correction.id = anchor_id
                              and correction.correction_mode = 'replace'
                        ) expected;
                    elsif anchor_type = 'membership_negative_closure_correction' then
                        select
                            array_agg(expected.entity_type order by expected.entity_type, expected.entity_id),
                            array_agg(expected.entity_id order by expected.entity_type, expected.entity_id)
                        into expected_types, expected_ids
                        from (
                            select 'membership_negative_closure_correction'::text as entity_type, anchor_id as entity_id
                            union all
                            select 'membership_negative_closure', correction.replacement_closure_id
                            from bodylife.membership_negative_closure_corrections correction
                            where correction.id = anchor_id
                              and correction.mode = 'replace'
                            union all
                            select 'membership_lifecycle_closure', lifecycle_closure.id
                            from bodylife.membership_negative_closure_corrections correction
                            join bodylife.membership_lifecycle_closures lifecycle_closure
                              on lifecycle_closure.negative_closure_id = correction.replacement_closure_id
                            where correction.id = anchor_id
                              and correction.mode = 'replace'
                            union all
                            select 'membership_negative_closure_line', line.id
                            from bodylife.membership_negative_closure_corrections correction
                            join bodylife.membership_negative_closure_lines line
                              on line.negative_closure_id = correction.replacement_closure_id
                            where correction.id = anchor_id
                              and correction.mode = 'replace'
                            union all
                            select 'membership_negative_closure_item', item.id
                            from bodylife.membership_negative_closure_corrections correction
                            join bodylife.membership_negative_closure_items item
                              on item.negative_closure_id = correction.replacement_closure_id
                            where correction.id = anchor_id
                              and correction.mode = 'replace'
                            union all
                            select 'visit_consumption', item.new_consumption_id
                            from bodylife.membership_negative_closure_corrections correction
                            join bodylife.membership_negative_closure_items item
                              on item.negative_closure_id = correction.replacement_closure_id
                            where correction.id = anchor_id
                              and correction.mode = 'replace'
                              and item.new_consumption_id is not null
                            union all
                            select 'payment', payment.id
                            from bodylife.membership_negative_closure_corrections correction
                            join bodylife.payments payment
                              on payment.negative_closure_id = correction.replacement_closure_id
                            where correction.id = anchor_id
                              and correction.mode = 'replace'
                        ) expected;
                    end if;

                    if not bodylife.paper_row_matches_expected_links(
                        target_row_id,
                        expected_types,
                        expected_ids) then
                        raise exception using
                            errcode = '23514',
                            constraint = 'ck_entry_batch_row_entities_exact_shape',
                            message = 'Paper row links do not match the complete canonical event aggregate.';
                    end if;
                end
                $function$;

                create function bodylife.enforce_paper_source_entity_link()
                returns trigger
                language plpgsql
                as $function$
                declare
                    target_entity_type text;
                    old_entity_id uuid;
                    new_entity_id uuid;
                    linked_row_id uuid;
                begin
                    target_entity_type := case tg_table_name
                        when 'visits' then 'visit'
                        when 'visit_cancellations' then 'visit_cancellation'
                        when 'payments' then 'payment'
                        when 'payment_corrections' then 'payment_correction'
                        when 'payment_cancellations' then 'payment_cancellation'
                        when 'freezes' then 'freeze'
                        when 'freeze_cancellations' then 'freeze_cancellation'
                        when 'issued_memberships' then 'membership'
                        when 'issued_membership_sale_corrections' then 'issued_membership_sale_correction'
                        when 'membership_negative_closures' then 'membership_negative_closure'
                        when 'membership_lifecycle_closures' then 'membership_lifecycle_closure'
                        when 'membership_negative_closure_corrections' then 'membership_negative_closure_correction'
                        when 'membership_negative_closure_lines' then 'membership_negative_closure_line'
                        when 'membership_negative_closure_items' then 'membership_negative_closure_item'
                        when 'visit_consumptions' then 'visit_consumption'
                    end;

                    if target_entity_type is null then
                        raise exception using
                            errcode = '23514',
                            constraint = 'ck_paper_source_trigger_table',
                            message = format('Unsupported paper source table: %s.', tg_table_name);
                    end if;

                    if tg_op <> 'INSERT' then
                        old_entity_id := (to_jsonb(old) ->> 'id')::uuid;
                        perform bodylife.assert_paper_entity_link(
                            target_entity_type,
                            old_entity_id);
                        for linked_row_id in
                            select link.entry_batch_row_id
                            from bodylife.entry_batch_row_entities link
                            where link.entity_type = target_entity_type
                              and link.entity_id = old_entity_id
                        loop
                            perform bodylife.assert_entry_batch_row_entity_links(linked_row_id);
                        end loop;
                    end if;

                    if tg_op <> 'DELETE' then
                        new_entity_id := (to_jsonb(new) ->> 'id')::uuid;
                        perform bodylife.assert_paper_entity_link(
                            target_entity_type,
                            new_entity_id);
                        for linked_row_id in
                            select link.entry_batch_row_id
                            from bodylife.entry_batch_row_entities link
                            where link.entity_type = target_entity_type
                              and link.entity_id = new_entity_id
                        loop
                            perform bodylife.assert_entry_batch_row_entity_links(linked_row_id);
                        end loop;
                    end if;

                    return null;
                end
                $function$;

                create function bodylife.enforce_entry_batch_row_entity_links()
                returns trigger
                language plpgsql
                as $function$
                begin
                    if tg_op <> 'INSERT' then
                        perform bodylife.assert_paper_entity_link(
                            old.entity_type,
                            old.entity_id);
                        perform bodylife.assert_entry_batch_row_entity_links(
                            old.entry_batch_row_id);
                    end if;

                    if tg_op <> 'DELETE' then
                        perform bodylife.assert_paper_entity_link(
                            new.entity_type,
                            new.entity_id);
                        perform bodylife.assert_entry_batch_row_entity_links(
                            new.entry_batch_row_id);
                    end if;

                    return null;
                end
                $function$;

                create function bodylife.protect_entry_batch_row_entity_links()
                returns trigger
                language plpgsql
                as $function$
                begin
                    raise exception using
                        errcode = '23514',
                        constraint = 'ck_entry_batch_row_entities_immutable',
                        message = 'A canonical paper row link is immutable.';
                end
                $function$;

                create function bodylife.protect_linked_entry_batch_row()
                returns trigger
                language plpgsql
                as $function$
                begin
                    if exists (
                        select 1
                        from bodylife.entry_batch_row_entities link
                        where link.entry_batch_row_id = old.id) then
                        raise exception using
                            errcode = '23514',
                            constraint = 'ck_linked_entry_batch_rows_immutable',
                            message = 'A linked paper fallback row is immutable.';
                    end if;

                    return case when tg_op = 'DELETE' then old else new end;
                end
                $function$;

                create constraint trigger ck_entry_batch_row_entities_exact_shape
                    after insert or update or delete
                    on bodylife.entry_batch_row_entities
                    deferrable initially deferred
                    for each row
                    execute function bodylife.enforce_entry_batch_row_entity_links();

                create trigger ck_entry_batch_row_entities_immutable
                    before update or delete
                    on bodylife.entry_batch_row_entities
                    for each row
                    execute function bodylife.protect_entry_batch_row_entity_links();

                create trigger ck_linked_entry_batch_rows_immutable
                    before update or delete
                    on bodylife.entry_batch_rows
                    for each row
                    execute function bodylife.protect_linked_entry_batch_row();

                create constraint trigger ck_visits_paper_link
                    after insert or update or delete on bodylife.visits
                    deferrable initially deferred for each row
                    execute function bodylife.enforce_paper_source_entity_link();
                create constraint trigger ck_visit_cancellations_paper_link
                    after insert or update or delete on bodylife.visit_cancellations
                    deferrable initially deferred for each row
                    execute function bodylife.enforce_paper_source_entity_link();
                create constraint trigger ck_payments_paper_link
                    after insert or update or delete on bodylife.payments
                    deferrable initially deferred for each row
                    execute function bodylife.enforce_paper_source_entity_link();
                create constraint trigger ck_payment_corrections_paper_link
                    after insert or update or delete on bodylife.payment_corrections
                    deferrable initially deferred for each row
                    execute function bodylife.enforce_paper_source_entity_link();
                create constraint trigger ck_payment_cancellations_paper_link
                    after insert or update or delete on bodylife.payment_cancellations
                    deferrable initially deferred for each row
                    execute function bodylife.enforce_paper_source_entity_link();
                create constraint trigger ck_freezes_paper_link
                    after insert or update or delete on bodylife.freezes
                    deferrable initially deferred for each row
                    execute function bodylife.enforce_paper_source_entity_link();
                create constraint trigger ck_freeze_cancellations_paper_link
                    after insert or update or delete on bodylife.freeze_cancellations
                    deferrable initially deferred for each row
                    execute function bodylife.enforce_paper_source_entity_link();
                create constraint trigger ck_memberships_paper_link
                    after insert or update or delete on bodylife.issued_memberships
                    deferrable initially deferred for each row
                    execute function bodylife.enforce_paper_source_entity_link();
                create constraint trigger ck_issued_sale_corrections_paper_link
                    after insert or update or delete on bodylife.issued_membership_sale_corrections
                    deferrable initially deferred for each row
                    execute function bodylife.enforce_paper_source_entity_link();
                create constraint trigger ck_negative_closures_paper_link
                    after insert or update or delete on bodylife.membership_negative_closures
                    deferrable initially deferred for each row
                    execute function bodylife.enforce_paper_source_entity_link();
                create constraint trigger ck_membership_lifecycle_closures_paper_link
                    after insert or update or delete on bodylife.membership_lifecycle_closures
                    deferrable initially deferred for each row
                    execute function bodylife.enforce_paper_source_entity_link();
                create constraint trigger ck_negative_closure_corrections_paper_link
                    after insert or update or delete on bodylife.membership_negative_closure_corrections
                    deferrable initially deferred for each row
                    execute function bodylife.enforce_paper_source_entity_link();
                create constraint trigger ck_negative_closure_lines_paper_link
                    after insert or update or delete on bodylife.membership_negative_closure_lines
                    deferrable initially deferred for each row
                    execute function bodylife.enforce_paper_source_entity_link();
                create constraint trigger ck_negative_closure_items_paper_link
                    after insert or update or delete on bodylife.membership_negative_closure_items
                    deferrable initially deferred for each row
                    execute function bodylife.enforce_paper_source_entity_link();
                create constraint trigger ck_visit_consumptions_paper_link
                    after insert or update or delete on bodylife.visit_consumptions
                    deferrable initially deferred for each row
                    execute function bodylife.enforce_paper_source_entity_link();
                """);
        }

        private static void RemovePaperFallbackLinkInvariants(
            MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                drop trigger if exists ck_visit_consumptions_paper_link
                    on bodylife.visit_consumptions;
                drop trigger if exists ck_negative_closure_items_paper_link
                    on bodylife.membership_negative_closure_items;
                drop trigger if exists ck_negative_closure_lines_paper_link
                    on bodylife.membership_negative_closure_lines;
                drop trigger if exists ck_negative_closure_corrections_paper_link
                    on bodylife.membership_negative_closure_corrections;
                drop trigger if exists ck_negative_closures_paper_link
                    on bodylife.membership_negative_closures;
                drop trigger if exists ck_membership_lifecycle_closures_paper_link
                    on bodylife.membership_lifecycle_closures;
                drop trigger if exists ck_issued_sale_corrections_paper_link
                    on bodylife.issued_membership_sale_corrections;
                drop trigger if exists ck_memberships_paper_link
                    on bodylife.issued_memberships;
                drop trigger if exists ck_freeze_cancellations_paper_link
                    on bodylife.freeze_cancellations;
                drop trigger if exists ck_freezes_paper_link
                    on bodylife.freezes;
                drop trigger if exists ck_payment_cancellations_paper_link
                    on bodylife.payment_cancellations;
                drop trigger if exists ck_payment_corrections_paper_link
                    on bodylife.payment_corrections;
                drop trigger if exists ck_payments_paper_link
                    on bodylife.payments;
                drop trigger if exists ck_visit_cancellations_paper_link
                    on bodylife.visit_cancellations;
                drop trigger if exists ck_visits_paper_link
                    on bodylife.visits;

                drop trigger if exists ck_linked_entry_batch_rows_immutable
                    on bodylife.entry_batch_rows;
                drop trigger if exists ck_entry_batch_row_entities_immutable
                    on bodylife.entry_batch_row_entities;
                drop trigger if exists ck_entry_batch_row_entities_exact_shape
                    on bodylife.entry_batch_row_entities;

                drop function if exists bodylife.protect_linked_entry_batch_row();
                drop function if exists bodylife.protect_entry_batch_row_entity_links();
                drop function if exists bodylife.enforce_entry_batch_row_entity_links();
                drop function if exists bodylife.enforce_paper_source_entity_link();
                drop function if exists bodylife.assert_entry_batch_row_entity_links(uuid);
                drop function if exists bodylife.assert_paper_entity_link(text, uuid);
                drop function if exists bodylife.paper_row_matches_expected_links(uuid, text[], uuid[]);
                drop function if exists bodylife.paper_entity_source_batch(text, uuid);
                """);
        }
    }
}
