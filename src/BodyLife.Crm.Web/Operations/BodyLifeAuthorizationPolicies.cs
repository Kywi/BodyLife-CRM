namespace BodyLife.Crm.Web.Operations;

public static class BodyLifeAuthorizationPolicies
{
    public const string OwnerOnly = "BodyLife.OwnerOnly";
    public const string AdminOrOwner = "BodyLife.AdminOrOwner";
    public const string CurrentOrOpenDayCorrection = "BodyLife.CurrentOrOpenDayCorrection";
    public const string AfterDayCloseCorrection = "BodyLife.AfterDayCloseCorrection";
}

public static class BodyLifeAccountTypes
{
    public const string Owner = "owner";
    public const string NamedAdmin = "named_admin";
    public const string SharedReceptionAdmin = "shared_reception_admin";
}

public static class BodyLifeRoles
{
    public const string Owner = "owner";
    public const string Admin = "admin";
}
