using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Modules.MembershipTypes;

namespace BodyLife.Crm.Web.Pages.Owner;

public sealed record EditMembershipTypeFormViewModel(
    EditMembershipTypeFormInput Input,
    IReadOnlyList<CommandError> Errors,
    bool IsOpen)
{
    public static EditMembershipTypeFormViewModel FromCatalog(
        MembershipTypeCatalogItem membershipType,
        IReadOnlyList<CommandError>? errors = null,
        bool isOpen = false)
    {
        ArgumentNullException.ThrowIfNull(membershipType);

        return new EditMembershipTypeFormViewModel(
            new EditMembershipTypeFormInput
            {
                MembershipTypeId = membershipType.MembershipTypeId,
                ExpectedUpdatedAt = membershipType.UpdatedAt,
                Name = membershipType.Name,
                DurationDays = membershipType.DurationDays,
                VisitsLimit = membershipType.VisitsLimit,
                PriceAmount = membershipType.Price.Amount,
                PriceCurrency = membershipType.Price.Currency,
                Comment = membershipType.Comment,
                IdempotencyKey = Guid.NewGuid().ToString("N"),
            },
            errors?.ToArray() ?? [],
            isOpen);
    }

    public static EditMembershipTypeFormViewModel FromSubmission(
        EditMembershipTypeFormInput input,
        IReadOnlyList<CommandError> errors)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(errors);

        return new EditMembershipTypeFormViewModel(
            input,
            errors.ToArray(),
            IsOpen: true);
    }
}

public sealed class EditMembershipTypeFormInput
{
    public Guid MembershipTypeId { get; set; }

    public DateTimeOffset ExpectedUpdatedAt { get; set; }

    public string? Name { get; set; }

    public int? DurationDays { get; set; }

    public int? VisitsLimit { get; set; }

    public decimal? PriceAmount { get; set; }

    public string? PriceCurrency { get; set; }

    public string? Comment { get; set; }

    public string? Reason { get; set; }

    public string? IdempotencyKey { get; set; }
}
