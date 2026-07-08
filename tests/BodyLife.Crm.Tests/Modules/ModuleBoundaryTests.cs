using BodyLife.Crm.Modules;

namespace BodyLife.Crm.Tests.Modules;

public sealed class ModuleBoundaryTests
{
    [Fact]
    public void AcceptedTopLevelModuleNamesAreVisibleAndUnique()
    {
        string[] expected =
        [
            "Clients/Search",
            "MembershipTypes",
            "Memberships",
            "Visits",
            "Payments",
            "Freezes",
            "NonWorkingDays",
            "Reports",
            "Audit",
            "Users/Roles",
        ];

        string[] actual =
        [
            ModuleNames.ClientsSearch,
            ModuleNames.MembershipTypes,
            ModuleNames.Memberships,
            ModuleNames.Visits,
            ModuleNames.Payments,
            ModuleNames.Freezes,
            ModuleNames.NonWorkingDays,
            ModuleNames.Reports,
            ModuleNames.Audit,
            ModuleNames.UsersRoles,
        ];

        Assert.Equal(expected.Order(StringComparer.Ordinal), actual.Order(StringComparer.Ordinal));
        Assert.Equal(actual.Length, actual.Distinct(StringComparer.Ordinal).Count());
    }
}
