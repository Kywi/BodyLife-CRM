namespace BodyLife.Crm.SharedKernel;

/// <summary>
/// Fixed business-calendar conversion for the gym. Persisted instants remain UTC.
/// </summary>
public static class BusinessTimeZone
{
    public const string IanaId = "Europe/Kyiv";
    public const string WindowsId = "FLE Standard Time";

    private static readonly TimeZoneInfo Kyiv = ResolveKyiv();

    public static DateTime ConvertInstantToLocal(DateTimeOffset instant)
    {
        ValidateInstant(instant, nameof(instant));
        var local = TimeZoneInfo.ConvertTime(instant, Kyiv).DateTime;
        if (local == DateTime.MinValue || local == DateTime.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(instant),
                "UTC instant is outside the supported business-calendar range.");
        }

        return local;
    }

    public static DateOnly GetBusinessDate(DateTimeOffset instant)
    {
        return DateOnly.FromDateTime(ConvertInstantToLocal(instant));
    }

    /// <summary>
    /// Normalizes a persisted UTC instant only when it can be represented in the
    /// fixed business calendar. Command boundaries use this instead of relying
    /// on conversion exceptions after a transaction has started.
    /// </summary>
    public static bool TryNormalizeUtcInstant(
        DateTimeOffset instant,
        out DateTimeOffset normalizedInstant)
    {
        normalizedInstant = default;
        if (instant == default
            || instant == DateTimeOffset.MinValue
            || instant == DateTimeOffset.MaxValue)
        {
            return false;
        }

        var normalized = instant.ToUniversalTime();
        try
        {
            var local = TimeZoneInfo.ConvertTime(normalized, Kyiv).DateTime;
            if (!IsSupportedBusinessDate(DateOnly.FromDateTime(local)))
            {
                return false;
            }
        }
        catch (ArgumentException)
        {
            return false;
        }

        normalizedInstant = normalized;
        return true;
    }

    public static bool IsSupportedBusinessDate(DateOnly businessDate)
    {
        return businessDate != default && businessDate != DateOnly.MaxValue;
    }

    public static UtcInstantRange GetUtcDayRange(DateOnly businessDate)
    {
        ValidateBusinessDate(businessDate, nameof(businessDate));
        var fromInclusive = ConvertLocalToUtc(
            businessDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified));
        var toExclusive = ConvertLocalToUtc(
            businessDate.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified));
        return new UtcInstantRange(fromInclusive, toExclusive);
    }

    public static DateTimeOffset ConvertLocalToUtc(DateTime localWallTime)
    {
        if (localWallTime.Kind != DateTimeKind.Unspecified)
        {
            throw new ArgumentException(
                "Business local wall time must have DateTimeKind.Unspecified.",
                nameof(localWallTime));
        }

        if (!IsSupportedBusinessDate(DateOnly.FromDateTime(localWallTime)))
        {
            throw new ArgumentOutOfRangeException(
                nameof(localWallTime),
                "Business local wall time is outside the supported range.");
        }

        if (Kyiv.IsInvalidTime(localWallTime))
        {
            throw new ArgumentException(
                "Business local wall time falls in the Europe/Kyiv daylight-saving gap.",
                nameof(localWallTime));
        }

        var offset = Kyiv.IsAmbiguousTime(localWallTime)
            ? Kyiv.GetAmbiguousTimeOffsets(localWallTime).Max()
            : Kyiv.GetUtcOffset(localWallTime);
        return new DateTimeOffset(localWallTime, offset).ToUniversalTime();
    }

    private static void ValidateInstant(DateTimeOffset instant, string parameterName)
    {
        if (!TryNormalizeUtcInstant(instant, out _))
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                "UTC instant is outside the supported business-calendar range.");
        }
    }

    private static void ValidateBusinessDate(DateOnly businessDate, string parameterName)
    {
        if (!IsSupportedBusinessDate(businessDate))
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                "Business date is outside the supported range.");
        }
    }

    private static TimeZoneInfo ResolveKyiv()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(IanaId);
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById(WindowsId);
        }
    }
}

public readonly record struct UtcInstantRange
{
    public UtcInstantRange(DateTimeOffset fromInclusive, DateTimeOffset toExclusive)
    {
        if (fromInclusive.Offset != TimeSpan.Zero
            || toExclusive.Offset != TimeSpan.Zero
            || fromInclusive >= toExclusive)
        {
            throw new ArgumentException("UTC day range must be ordered UTC instants.");
        }

        FromInclusive = fromInclusive;
        ToExclusive = toExclusive;
    }

    public DateTimeOffset FromInclusive { get; }

    public DateTimeOffset ToExclusive { get; }
}
