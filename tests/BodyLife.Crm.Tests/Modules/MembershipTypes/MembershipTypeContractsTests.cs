using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.Modules.MembershipTypes;
using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Tests.Modules.MembershipTypes;

public sealed class MembershipTypeContractsTests
{
    private static readonly DateTimeOffset UpdatedAt = new(
        2026,
        7,
        12,
        10,
        0,
        0,
        TimeSpan.Zero);

    [Fact]
    public void CreateCommandDefaultsToActiveAndCarriesFutureCatalogValues()
    {
        var envelope = CreateEnvelope();
        var command = new CreateMembershipTypeCommand(
            envelope,
            "Eight visits",
            30,
            8,
            new Money(1000m, "UAH"),
            "Standard catalog type");

        Assert.IsAssignableFrom<IBodyLifeCommand>(command);
        Assert.Same(envelope, command.Envelope);
        Assert.True(command.IsActive);
        Assert.Equal(30, command.DurationDays);
        Assert.Equal(8, command.VisitsLimit);
        Assert.Equal(new Money(1000m, "UAH"), command.Price);
    }

    [Fact]
    public void CreateCommandCanExplicitlyStartInactive()
    {
        var command = new CreateMembershipTypeCommand(
            CreateEnvelope(),
            "Prepared later",
            14,
            4,
            new Money(500m, "UAH"),
            Comment: null,
            IsActive: false);

        Assert.False(command.IsActive);
    }

    [Fact]
    public void EditAndDeactivateCarryExpectedVersionWhileLifecycleStaysSeparate()
    {
        var membershipTypeId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var edit = new EditMembershipTypeCommand(
            CreateEnvelope(reason: "Correct future sale terms"),
            membershipTypeId,
            UpdatedAt,
            "Ten visits",
            45,
            10,
            new Money(1400m, "UAH"),
            "Future sales only");
        var deactivateEnvelope = CreateEnvelope(reason: "Retired catalog offer");
        var deactivate = new DeactivateMembershipTypeCommand(
            deactivateEnvelope,
            membershipTypeId,
            UpdatedAt);

        Assert.Equal(UpdatedAt, edit.ExpectedUpdatedAt);
        Assert.DoesNotContain(
            typeof(EditMembershipTypeCommand).GetProperties(),
            property => property.Name == "IsActive");
        Assert.Equal(UpdatedAt, deactivate.ExpectedUpdatedAt);
        Assert.Equal("Retired catalog offer", deactivate.Envelope.Reason);
    }

    [Fact]
    public void PublicMembershipTypeCommandsExposeRequiredWorkflowsWithoutHardDelete()
    {
        var commandNames = typeof(MembershipTypesModule).Assembly
            .GetExportedTypes()
            .Where(type => type.Namespace?.StartsWith(
                typeof(MembershipTypesModule).Namespace!,
                StringComparison.Ordinal) == true)
            .Where(type => typeof(IBodyLifeCommand).IsAssignableFrom(type))
            .Select(type => type.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Contains(nameof(CreateMembershipTypeCommand), commandNames);
        Assert.Contains(nameof(DeactivateMembershipTypeCommand), commandNames);
        Assert.Contains(nameof(EditMembershipTypeCommand), commandNames);
        Assert.DoesNotContain(
            commandNames,
            commandName => commandName.Contains("Delete", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void IssueQueryDefaultsToOrdinaryActiveOnlyContext()
    {
        var actor = CreateEnvelope().Actor;

        var query = new GetMembershipTypesForIssueQuery(actor);

        Assert.Same(actor, query.Actor);
        Assert.False(query.IncludeInactive);
    }

    [Fact]
    public void OrdinaryIssueResultRejectsInactiveCatalogRows()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            GetMembershipTypesForIssueResult.Succeeded(
                [CreateCatalogItem(isActive: false)],
                includeInactive: false,
                QueryPermissionSet.Empty));

        Assert.Equal("items", exception.ParamName);
    }

    [Fact]
    public void OwnerCatalogContextMayIncludeInactiveRowsAndActions()
    {
        var permissions = new QueryPermissionSet(
        [
            QueryPermissionResult.Allowed(
                MembershipTypeCatalogActionKeys.Create,
                MembershipTypeCatalogActionKeys.OwnerPolicy),
            QueryPermissionResult.Allowed(
                MembershipTypeCatalogActionKeys.Edit,
                MembershipTypeCatalogActionKeys.OwnerPolicy),
            QueryPermissionResult.Allowed(
                MembershipTypeCatalogActionKeys.Deactivate,
                MembershipTypeCatalogActionKeys.OwnerPolicy),
        ]);
        var active = CreateCatalogItem(isActive: true);
        var inactive = CreateCatalogItem(isActive: false);

        var result = GetMembershipTypesForIssueResult.Succeeded(
            [active, inactive],
            includeInactive: true,
            permissions);

        Assert.Equal(GetMembershipTypesForIssueStatus.Success, result.Status);
        Assert.Equal([active, inactive], result.Items);
        Assert.True(active.IsAvailableForOrdinaryIssue);
        Assert.False(inactive.IsAvailableForOrdinaryIssue);
        Assert.True(result.AllowedActions.IsAllowed(MembershipTypeCatalogActionKeys.Create));
        Assert.True(result.AllowedActions.IsAllowed(MembershipTypeCatalogActionKeys.Edit));
        Assert.True(result.AllowedActions.IsAllowed(MembershipTypeCatalogActionKeys.Deactivate));
    }

    [Fact]
    public void DeniedQueryResultContainsNoCatalogRowsOrActions()
    {
        var result = GetMembershipTypesForIssueResult.Denied("  Owner access is required.  ");

        Assert.Equal(GetMembershipTypesForIssueStatus.PermissionDenied, result.Status);
        Assert.Equal("permission_denied", result.ErrorCode);
        Assert.Equal("Owner access is required.", result.ErrorMessage);
        Assert.Empty(result.Items);
        Assert.Empty(result.AllowedActions.Items);
    }

    [Fact]
    public void CatalogActionKeysUseStableOwnerPolicyContract()
    {
        Assert.Equal("membership_types.create", MembershipTypeCatalogActionKeys.Create);
        Assert.Equal("membership_types.edit", MembershipTypeCatalogActionKeys.Edit);
        Assert.Equal("membership_types.deactivate", MembershipTypeCatalogActionKeys.Deactivate);
        Assert.Equal("BodyLife.OwnerOnly", MembershipTypeCatalogActionKeys.OwnerPolicy);
    }

    private static MembershipTypeCatalogItem CreateCatalogItem(bool isActive)
    {
        return new MembershipTypeCatalogItem(
            Guid.NewGuid(),
            "Eight visits",
            30,
            8,
            new Money(1000m, "UAH"),
            isActive,
            Comment: null,
            CreatedAt: UpdatedAt.AddDays(-1),
            UpdatedAt,
            DeactivatedAt: isActive ? null : UpdatedAt);
    }

    private static CommandEnvelope CreateEnvelope(string? reason = null)
    {
        var actor = new ActorContext(
            AccountId.New(),
            ActorRole.Owner,
            AccountKind.Owner,
            SessionId.New(),
            "owner tablet");

        return new CommandEnvelope(
            actor,
            new RequestCorrelationId("membership-type-contract"),
            EntryOrigin.Normal,
            OccurredAt: null,
            IdempotencyKey: "membership-type-key",
            reason,
            Comment: null);
    }
}
