namespace BodyLife.Crm.Web.Pages.Audit;

internal static class AuditTimestampPrecision
{
    private const long TicksPerMicrosecond = TimeSpan.TicksPerMillisecond / 1000;

    internal static bool IsSamePostgreSqlInstant(
        DateTimeOffset left,
        DateTimeOffset right)
    {
        return CompareAtPostgreSqlPrecision(left, right) == 0;
    }

    internal static int CompareAtPostgreSqlPrecision(
        DateTimeOffset left,
        DateTimeOffset right)
    {
        return PostgreSqlTicks(left).CompareTo(PostgreSqlTicks(right));
    }

    private static long PostgreSqlTicks(DateTimeOffset value)
    {
        var utcTicks = value.UtcDateTime.Ticks;
        return utcTicks - (utcTicks % TicksPerMicrosecond);
    }
}
