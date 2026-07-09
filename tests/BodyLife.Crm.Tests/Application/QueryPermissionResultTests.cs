using BodyLife.Crm.Application.Queries;

namespace BodyLife.Crm.Tests.Application;

public sealed class QueryPermissionResultTests
{
    [Fact]
    public void AllowedCreatesAllowedPermissionWithoutDeniedReason()
    {
        var permission = QueryPermissionResult.Allowed(
            " issue_membership ",
            " BodyLife.AdminOrOwner ");

        Assert.Equal("issue_membership", permission.ActionKey);
        Assert.Equal("BodyLife.AdminOrOwner", permission.RequiredPolicy);
        Assert.True(permission.IsAllowed);
        Assert.Null(permission.DeniedReasonCode);
        Assert.Null(permission.DeniedReason);
    }

    [Fact]
    public void DeniedCreatesDeniedPermissionWithReason()
    {
        var permission = QueryPermissionResult.Denied(
            " edit_membership_type ",
            " BodyLife.OwnerOnly ",
            " permission_denied ",
            " Owner required. ");

        Assert.Equal("edit_membership_type", permission.ActionKey);
        Assert.Equal("BodyLife.OwnerOnly", permission.RequiredPolicy);
        Assert.False(permission.IsAllowed);
        Assert.Equal(QueryPermissionDeniedReasonCodes.PermissionDenied, permission.DeniedReasonCode);
        Assert.Equal("Owner required.", permission.DeniedReason);
    }

    [Fact]
    public void QueryPermissionSetLooksUpAllowedActions()
    {
        var permissions = new QueryPermissionSet(
        [
            QueryPermissionResult.Allowed("search_clients", "BodyLife.AdminOrOwner"),
            QueryPermissionResult.Denied(
                "edit_membership_type",
                "BodyLife.OwnerOnly",
                QueryPermissionDeniedReasonCodes.PermissionDenied,
                "Owner required."),
        ]);

        Assert.True(permissions.IsAllowed("search_clients"));
        Assert.False(permissions.IsAllowed("edit_membership_type"));
        Assert.False(permissions.IsAllowed("unknown_action"));
        Assert.True(permissions.TryGet("edit_membership_type", out var deniedPermission));
        Assert.Equal("BodyLife.OwnerOnly", deniedPermission.RequiredPolicy);
    }

    [Fact]
    public void QueryPermissionSetRejectsDuplicateActionKeys()
    {
        var exception = Assert.Throws<ArgumentException>(() => new QueryPermissionSet(
        [
            QueryPermissionResult.Allowed("search_clients", "BodyLife.AdminOrOwner"),
            QueryPermissionResult.Denied(
                "search_clients",
                "BodyLife.OwnerOnly",
                QueryPermissionDeniedReasonCodes.PermissionDenied,
                "Owner required."),
        ]));

        Assert.Contains("Duplicate permission action key", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void PermissionResultRejectsBlankActionKeys(string actionKey)
    {
        Assert.Throws<ArgumentException>(() => QueryPermissionResult.Allowed(
            actionKey,
            "BodyLife.AdminOrOwner"));
    }
}
