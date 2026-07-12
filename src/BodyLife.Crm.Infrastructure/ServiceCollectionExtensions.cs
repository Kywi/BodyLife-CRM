using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.Infrastructure.Persistence;
using BodyLife.Crm.Infrastructure.Persistence.Audit;
using BodyLife.Crm.Infrastructure.Persistence.ClientsSearch;
using BodyLife.Crm.Infrastructure.Persistence.UsersRoles;
using BodyLife.Crm.Modules.Clients.Search;
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
        services.TryAddSingleton<PasswordHashingService>();
        services.AddScoped<BusinessAuditAppender>();
        services.AddScoped<
            IBodyLifeQueryHandler<FindClientDuplicateCandidatesQuery, IReadOnlyList<ClientDuplicateCandidate>>,
            FindClientDuplicateCandidatesQueryHandler>();
        services.AddScoped<
            IBodyLifeQueryHandler<SearchClientsQuery, SearchClientsResult>,
            SearchClientsQueryHandler>();
        services.AddScoped<
            IBodyLifeQueryHandler<GetClientProfileQuery, GetClientProfileResult>,
            GetClientProfileQueryHandler>();
        services.AddScoped<IBodyLifeCommandHandler<CreateClientCommand>, CreateClientCommandHandler>();
        services.AddScoped<IBodyLifeCommandHandler<UpdateClientCommand>, UpdateClientCommandHandler>();
        services.AddScoped<
            IBodyLifeCommandHandler<AssignOrChangeCardCommand>,
            AssignOrChangeCardCommandHandler>();
        services.AddScoped<AccountLoginService>();
        services.AddScoped<AccountSessionValidationService>();
        services.AddScoped<OwnerCredentialsBootstrapper>();
        services.AddScoped<OwnerBootstrapper>();
        services.AddScoped<StaffAccountLifecycleService>();
        services.AddScoped<StaffAccountQueryService>();
        services.AddScoped<StaffCredentialsService>();

        return services;
    }
}
