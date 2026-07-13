using BodyLife.Crm.Application.Commands;

namespace BodyLife.Crm.Web.Pages.Owner;

public sealed record CreateMembershipTypeFormViewModel(
    CreateMembershipTypeFormInput Input,
    IReadOnlyList<CommandError> Errors)
{
    public static CreateMembershipTypeFormViewModel New()
    {
        return new CreateMembershipTypeFormViewModel(
            new CreateMembershipTypeFormInput
            {
                PriceCurrency = "UAH",
                IdempotencyKey = Guid.NewGuid().ToString("N"),
            },
            Errors: []);
    }

    public static CreateMembershipTypeFormViewModel FromSubmission(
        CreateMembershipTypeFormInput input,
        IReadOnlyList<CommandError> errors)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(errors);

        return new CreateMembershipTypeFormViewModel(input, errors.ToArray());
    }
}

public sealed class CreateMembershipTypeFormInput
{
    public string? Name { get; set; }

    public int? DurationDays { get; set; }

    public int? VisitsLimit { get; set; }

    public decimal? PriceAmount { get; set; }

    public string? PriceCurrency { get; set; }

    public string? Comment { get; set; }

    public string? IdempotencyKey { get; set; }
}
