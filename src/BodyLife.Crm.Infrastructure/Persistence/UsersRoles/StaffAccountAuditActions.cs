namespace BodyLife.Crm.Infrastructure.Persistence.UsersRoles;

public static class StaffAccountAuditActions
{
    public const string EntityType = "staff_account";
    public const string Created = "staff_account.created";
    public const string DisplayNameUpdated = "staff_account.display_name_updated";
    public const string Activated = "staff_account.activated";
    public const string Deactivated = "staff_account.deactivated";
    public const string CredentialsConfigured = "staff_credentials.configured";
    public const string CredentialsReset = "staff_credentials.reset";
}
