using BodyLife.Crm.Infrastructure.Persistence;
using BodyLife.Crm.Infrastructure.Persistence.UsersRoles;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BodyLife.Crm.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddBodyLifePersistence(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(BodyLifeDbContextOptions.ConnectionStringName);

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"ConnectionStrings:{BodyLifeDbContextOptions.ConnectionStringName} must be configured.");
        }

        services.AddDbContext<BodyLifeDbContext>(
            options => BodyLifeDbContextOptions.Configure(options, connectionString));
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<OwnerBootstrapper>();

        return services;
    }
}
