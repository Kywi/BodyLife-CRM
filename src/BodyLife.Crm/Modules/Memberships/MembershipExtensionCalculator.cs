namespace BodyLife.Crm.Modules.Memberships;

public static class MembershipExtensionCalculator
{
    public static MembershipExtensionCalculation Calculate(
        IEnumerable<MembershipExtensionSourceRange>? sources)
    {
        ArgumentNullException.ThrowIfNull(sources);

        var sourceIdentities = new HashSet<(string SourceType, Guid SourceId)>();
        var activeDates = new HashSet<DateOnly>();
        var explanationDays = new List<MembershipExtensionDay>();

        foreach (var source in sources)
        {
            if (source is null)
            {
                throw new ArgumentException(
                    "Extension sources cannot contain a missing item.",
                    nameof(sources));
            }

            if (!sourceIdentities.Add((source.SourceType, source.SourceId)))
            {
                throw new ArgumentException(
                    "Each extension source type and id pair must be unique.",
                    nameof(sources));
            }

            for (var dayNumber = source.Range.StartDate.DayNumber;
                 dayNumber <= source.Range.EndDate.DayNumber;
                 dayNumber++)
            {
                var extensionDate = DateOnly.FromDayNumber(dayNumber);
                explanationDays.Add(new MembershipExtensionDay(extensionDate, source));

                if (source.IsActive)
                {
                    activeDates.Add(extensionDate);
                }
            }
        }

        explanationDays.Sort(CompareExplanationDays);

        return new MembershipExtensionCalculation(
            activeDates.Count,
            explanationDays);
    }

    private static int CompareExplanationDays(
        MembershipExtensionDay left,
        MembershipExtensionDay right)
    {
        var comparison = left.ExtensionDate.CompareTo(right.ExtensionDate);
        if (comparison != 0)
        {
            return comparison;
        }

        comparison = right.IsActive.CompareTo(left.IsActive);
        if (comparison != 0)
        {
            return comparison;
        }

        comparison = string.CompareOrdinal(left.SourceType, right.SourceType);
        if (comparison != 0)
        {
            return comparison;
        }

        comparison = left.SourceId.CompareTo(right.SourceId);
        return comparison != 0
            ? comparison
            : string.CompareOrdinal(left.SourceLabel, right.SourceLabel);
    }
}
