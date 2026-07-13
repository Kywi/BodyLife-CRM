using BodyLife.Crm.Modules.MembershipTypes;
using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Modules.Memberships;

public sealed record IssuedMembershipSnapshot
{
    public IssuedMembershipSnapshot(
        string typeName,
        int durationDays,
        int visitsLimit,
        Money price)
    {
        var catalogValues = MembershipTypeCatalogRules.NormalizeAndValidate(
            typeName,
            durationDays,
            visitsLimit,
            price,
            comment: null);

        TypeName = catalogValues.Name;
        DurationDays = catalogValues.DurationDays;
        VisitsLimit = catalogValues.VisitsLimit;
        Price = catalogValues.Price;
    }

    public string TypeName { get; }

    public int DurationDays { get; }

    public int VisitsLimit { get; }

    public Money Price { get; }
}
