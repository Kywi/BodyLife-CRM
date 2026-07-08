namespace BodyLife.Crm.Infrastructure.Tests.PostgreSql;

public sealed class PostgreSqlFactAttribute : FactAttribute
{
    public PostgreSqlFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(PostgreSqlTestDatabase.AdminConnectionString))
        {
            Skip = $"{PostgreSqlTestDatabase.AdminConnectionStringEnvironmentVariable} is not configured.";
        }
    }
}
