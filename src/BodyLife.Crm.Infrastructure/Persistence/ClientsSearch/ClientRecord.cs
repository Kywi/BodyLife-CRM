namespace BodyLife.Crm.Infrastructure.Persistence.ClientsSearch;

internal sealed class ClientRecord
{
    public Guid Id { get; set; }

    public required string Surname { get; set; }

    public required string Name { get; set; }

    public string? Patronymic { get; set; }

    public required string NormalizedFullName { get; set; }

    public string? PhoneRaw { get; set; }

    public string? PhoneNormalized { get; set; }

    public string? PhoneLastFour { get; set; }

    public string? Comment { get; set; }

    public required string OperationalStatus { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid CreatedByAccountId { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
