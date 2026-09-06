using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BodyLife.Crm.Infrastructure.Persistence.Migrations;

public partial class InitialBaseline
{
    private static void AddMembershipLifecycleInvariants(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        create function bodylife.assert_membership_lifecycle_closure(target_membership_id uuid) returns void language plpgsql as $f$
        declare membership_status text; closure_count bigint;
        begin
          select status into membership_status from bodylife.issued_memberships where id = target_membership_id;
          if not found then return; end if;
          select count(*) into closure_count from bodylife.membership_lifecycle_closures where source_membership_id = target_membership_id;
          if (membership_status = 'closed' and closure_count <> 1) or (membership_status in ('active','canceled','corrected') and closure_count <> 0) then
            raise exception using errcode = '23514', constraint = 'ck_issued_memberships_lifecycle_closure', message = 'Issued Membership lifecycle status requires its matching closure fact.';
          end if;
          if membership_status = 'closed' and exists (
            select 1 from bodylife.membership_lifecycle_closures closure
            where closure.source_membership_id = target_membership_id
              and closure.reason_code = 'one_off_zero_balance'
              and (not exists (select 1 from bodylife.membership_negative_closures negative_closure where negative_closure.id = closure.negative_closure_id and negative_closure.closure_type = 'one_off')
                   or not exists (select 1 from bodylife.membership_negative_closure_items item where item.negative_closure_id = closure.negative_closure_id and item.source_membership_id = closure.source_membership_id))) then
            raise exception using errcode = '23514', constraint = 'ck_membership_lifecycle_closure_one_off_provenance', message = 'One-off lifecycle closure requires matching historical one-off coverage.';
          end if;
        end $f$;
        create function bodylife.enforce_membership_lifecycle_closure() returns trigger language plpgsql as $f$
        begin
          if tg_table_name = 'issued_memberships' then
            perform bodylife.assert_membership_lifecycle_closure(case when tg_op = 'DELETE' then old.id else new.id end);
          else
            perform bodylife.assert_membership_lifecycle_closure(case when tg_op = 'DELETE' then old.source_membership_id else new.source_membership_id end);
          end if;
          return null;
        end $f$;
        create constraint trigger ck_issued_memberships_lifecycle_closure after insert or update or delete on bodylife.issued_memberships deferrable initially deferred for each row execute function bodylife.enforce_membership_lifecycle_closure();
        create constraint trigger ck_membership_lifecycle_closures_status after insert or update or delete on bodylife.membership_lifecycle_closures deferrable initially deferred for each row execute function bodylife.enforce_membership_lifecycle_closure();
        create function bodylife.enforce_membership_lifecycle_one_off_provenance() returns trigger language plpgsql as $f$
        declare target_negative_closure_id uuid; source_id uuid;
        begin
          if tg_table_name = 'membership_negative_closures' then
            target_negative_closure_id := (case when tg_op = 'DELETE' then to_jsonb(old) else to_jsonb(new) end ->> 'id')::uuid;
          else
            target_negative_closure_id := (case when tg_op = 'DELETE' then to_jsonb(old) else to_jsonb(new) end ->> 'negative_closure_id')::uuid;
          end if;
          for source_id in select source_membership_id from bodylife.membership_lifecycle_closures where negative_closure_id = target_negative_closure_id loop perform bodylife.assert_membership_lifecycle_closure(source_id); end loop;
          return null;
        end $f$;
        create constraint trigger ck_negative_closures_lifecycle_provenance after update or delete on bodylife.membership_negative_closures deferrable initially deferred for each row execute function bodylife.enforce_membership_lifecycle_one_off_provenance();
        create constraint trigger ck_negative_closure_items_lifecycle_provenance after update or delete on bodylife.membership_negative_closure_items deferrable initially deferred for each row execute function bodylife.enforce_membership_lifecycle_one_off_provenance();
        create function bodylife.protect_membership_lifecycle_closure() returns trigger language plpgsql as $f$ begin raise exception using errcode = '23514', constraint = 'ck_membership_lifecycle_closures_append_only', message = 'Membership lifecycle closure facts are append-only.'; end $f$;
        create trigger ck_membership_lifecycle_closures_append_only before update or delete on bodylife.membership_lifecycle_closures for each statement execute function bodylife.protect_membership_lifecycle_closure();
        create trigger ck_membership_lifecycle_closures_no_truncate before truncate on bodylife.membership_lifecycle_closures for each statement execute function bodylife.protect_membership_lifecycle_closure();
        """);

    private static void RemoveMembershipLifecycleInvariants(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        drop trigger if exists ck_membership_lifecycle_closures_append_only on bodylife.membership_lifecycle_closures;
        drop trigger if exists ck_membership_lifecycle_closures_no_truncate on bodylife.membership_lifecycle_closures;
        drop trigger if exists ck_membership_lifecycle_closures_status on bodylife.membership_lifecycle_closures;
        drop trigger if exists ck_negative_closures_lifecycle_provenance on bodylife.membership_negative_closures;
        drop trigger if exists ck_negative_closure_items_lifecycle_provenance on bodylife.membership_negative_closure_items;
        drop trigger if exists ck_issued_memberships_lifecycle_closure on bodylife.issued_memberships;
        drop function if exists bodylife.protect_membership_lifecycle_closure();
        drop function if exists bodylife.enforce_membership_lifecycle_closure();
        drop function if exists bodylife.enforce_membership_lifecycle_one_off_provenance();
        drop function if exists bodylife.assert_membership_lifecycle_closure(uuid);
        """);
}
