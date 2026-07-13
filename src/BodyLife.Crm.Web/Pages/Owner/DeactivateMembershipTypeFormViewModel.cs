using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Modules.MembershipTypes;

namespace BodyLife.Crm.Web.Pages.Owner;

public sealed record DeactivateMembershipTypeFormViewModel(
    DeactivateMembershipTypeFormInput Input,
    IReadOnlyList<CommandError> Errors,
    bool IsOpen)
{
    public static DeactivateMembershipTypeFormViewModel FromCatalog(
        MembershipTypeCatalogItem membershipType,
        IReadOnlyList<CommandError>? errors = null,
        bool isOpen = false)
    {
        ArgumentNullException.ThrowIfNull(membershipType);

        if (!membershipType.IsActive)
        {
            throw new ArgumentException(
                "Only an active membership type can have a deactivation form.",
                nameof(membershipType));
        }

        return new DeactivateMembershipTypeFormViewModel(
            new DeactivateMembershipTypeFormInput
            {
                MembershipTypeId = membershipType.MembershipTypeId,
                ExpectedUpdatedAt = membershipType.UpdatedAt,
                IdempotencyKey = Guid.NewGuid().ToString("N"),
            },
            errors?.ToArray() ?? [],
            isOpen);
    }

    public static DeactivateMembershipTypeFormViewModel FromSubmission(
        DeactivateMembershipTypeFormInput input,
        IReadOnlyList<CommandError> errors)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(errors);

        return new DeactivateMembershipTypeFormViewModel(
            input,
            errors.ToArray(),
            IsOpen: true);
    }
}

public sealed class DeactivateMembershipTypeFormInput
{
    public Guid MembershipTypeId { get; set; }

    public DateTimeOffset ExpectedUpdatedAt { get; set; }

    public string? Reason { get; set; }

    public string? IdempotencyKey { get; set; }
}
