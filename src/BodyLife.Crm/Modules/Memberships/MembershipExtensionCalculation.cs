namespace BodyLife.Crm.Modules.Memberships;

public sealed record MembershipExtensionCalculation
{
    internal MembershipExtensionCalculation(
        int extensionDays,
        IEnumerable<MembershipExtensionDay> explanationDays)
    {
        ExtensionDays = extensionDays;
        ExplanationDays = Array.AsReadOnly(explanationDays.ToArray());
    }

    public int ExtensionDays { get; }

    public IReadOnlyList<MembershipExtensionDay> ExplanationDays { get; }
}
