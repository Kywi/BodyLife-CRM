namespace BodyLife.Crm.Modules.Clients.Search;

public sealed record ClientProfileMembershipArea(
    ClientMembershipSummary? CurrentMembership,
    IReadOnlyList<ClientMembershipSummary> Timeline,
    IReadOnlyList<ClientWarning> Warnings)
{
    public static ClientProfileMembershipArea Empty { get; } = new(
        CurrentMembership: null,
        Timeline: [],
        Warnings: []);
}
