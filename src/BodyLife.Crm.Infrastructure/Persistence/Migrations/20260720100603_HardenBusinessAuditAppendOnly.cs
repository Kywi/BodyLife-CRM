using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BodyLife.Crm.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HardenBusinessAuditAppendOnly : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                drop trigger trg_business_audit_entries_append_only
                    on bodylife.business_audit_entries;
                drop function bodylife.reject_business_audit_entry_mutation();
                """);
        }
    }
}
