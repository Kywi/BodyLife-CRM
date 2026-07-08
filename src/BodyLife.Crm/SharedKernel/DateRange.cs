namespace BodyLife.Crm.SharedKernel;

public readonly record struct DateRange
{
    public DateRange(DateOnly startDate, DateOnly endDate)
    {
        if (endDate < startDate)
        {
            throw new ArgumentException("End date must be on or after start date.", nameof(endDate));
        }

        StartDate = startDate;
        EndDate = endDate;
    }

    public DateOnly StartDate { get; }

    public DateOnly EndDate { get; }

    public int InclusiveDays => EndDate.DayNumber - StartDate.DayNumber + 1;

    public bool Contains(DateOnly date) => StartDate <= date && date <= EndDate;
}
