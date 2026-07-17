using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.Infrastructure.Persistence;
using BodyLife.Crm.Infrastructure.Persistence.Audit;
using BodyLife.Crm.Infrastructure.Persistence.ClientsSearch;
using BodyLife.Crm.Infrastructure.Persistence.Freezes;
using BodyLife.Crm.Infrastructure.Persistence.Memberships;
using BodyLife.Crm.Infrastructure.Persistence.MembershipTypes;
using BodyLife.Crm.Infrastructure.Persistence.NonWorkingDays;
using BodyLife.Crm.Infrastructure.Persistence.Payments;
using BodyLife.Crm.Infrastructure.Persistence.UsersRoles;
using BodyLife.Crm.Infrastructure.Persistence.Visits;
using BodyLife.Crm.Modules.Clients.Search;
using BodyLife.Crm.Modules.Freezes;
using BodyLife.Crm.Modules.Memberships;
using BodyLife.Crm.Modules.MembershipTypes;
using BodyLife.Crm.Modules.NonWorkingDays;
using BodyLife.Crm.Modules.Payments;
using BodyLife.Crm.Modules.Visits;
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
        services.AddSingleton<INonWorkingDayPreviewTokenService>(provider =>
            new HmacNonWorkingDayPreviewTokenService(
                NonWorkingDayPreviewTokenOptions.FromConfiguration(configuration),
                provider.GetRequiredService<TimeProvider>()));
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
        services.AddScoped<
            IBodyLifeQueryHandler<GetMembershipTypesForIssueQuery, GetMembershipTypesForIssueResult>,
            GetMembershipTypesForIssueQueryHandler>();
        services.AddScoped<
            IBodyLifeQueryHandler<GetMembershipStateQuery, GetMembershipStateResult>,
            GetMembershipStateQueryHandler>();
        services.AddScoped<
            IBodyLifeQueryHandler<GetClientMembershipStatesQuery, GetClientMembershipStatesResult>,
            GetClientMembershipStatesQueryHandler>();
        services.AddScoped<
            IBodyLifeQueryHandler<PreviewIssueMembershipQuery, PreviewIssueMembershipResult>,
            PreviewIssueMembershipQueryHandler>();
        services.AddScoped<
            IBodyLifeQueryHandler<GetMarkVisitOptionsQuery, GetMarkVisitOptionsResult>,
            GetMarkVisitOptionsQueryHandler>();
        services.AddScoped<
            IBodyLifeQueryHandler<GetClientVisitRowsQuery, GetClientVisitRowsResult>,
            GetClientVisitRowsQueryHandler>();
        services.AddScoped<
            IBodyLifeQueryHandler<
                GetDailyVisitSourceRowsQuery,
                GetDailyVisitSourceRowsResult>,
            GetDailyVisitSourceRowsQueryHandler>();
        services.AddScoped<
            IBodyLifeQueryHandler<
                GetClientPaymentRowsQuery,
                GetClientPaymentRowsResult>,
            GetClientPaymentRowsQueryHandler>();
        services.AddScoped<
            IBodyLifeQueryHandler<
                GetDailyPaymentSourceRowsQuery,
                GetDailyPaymentSourceRowsResult>,
            GetDailyPaymentSourceRowsQueryHandler>();
        services.AddScoped<IBodyLifeCommandHandler<CreateClientCommand>, CreateClientCommandHandler>();
        services.AddScoped<
            IBodyLifeCommandHandler<CreateMembershipTypeCommand>,
            CreateMembershipTypeCommandHandler>();
        services.AddScoped<
            IBodyLifeCommandHandler<DeactivateMembershipTypeCommand>,
            DeactivateMembershipTypeCommandHandler>();
        services.AddScoped<
            IBodyLifeCommandHandler<EditMembershipTypeCommand>,
            EditMembershipTypeCommandHandler>();
        services.AddScoped<IBodyLifeCommandHandler<UpdateClientCommand>, UpdateClientCommandHandler>();
        services.AddScoped<
            IBodyLifeCommandHandler<AssignOrChangeCardCommand>,
            AssignOrChangeCardCommandHandler>();
        services.AddScoped<
            IBodyLifeCommandHandler<CreateMembershipOpeningStateCommand>,
            CreateMembershipOpeningStateCommandHandler>();
        services.AddScoped<
            IMembershipIssuePaymentWriter,
            MembershipIssuePaymentWriter>();
        services.AddScoped<
            IBodyLifeCommandHandler<IssueMembershipCommand>,
            IssueMembershipCommandHandler>();
        services.AddScoped<
            IBodyLifeCommandHandler<MarkVisitCommand>,
            MarkVisitCommandHandler>();
        services.AddScoped<
            IBodyLifeCommandHandler<CancelVisitCommand>,
            CancelVisitCommandHandler>();
        services.AddScoped<
            IBodyLifeCommandHandler<CreatePaymentCommand>,
            CreatePaymentCommandHandler>();
        services.AddScoped<
            IBodyLifeCommandHandler<CorrectPaymentCommand>,
            CorrectPaymentCommandHandler>();
        services.AddScoped<
            IBodyLifeCommandHandler<AddFreezeCommand>,
            AddFreezeCommandHandler>();
        services.AddScoped<
            IBodyLifeCommandHandler<CancelFreezeCommand>,
            CancelFreezeCommandHandler>();
        services.TryAddSingleton<
            IPaymentDayReconciliationStatusProvider,
            OpenPaymentDayReconciliationStatusProvider>();
        services.TryAddSingleton<
            IVisitDayReconciliationStatusProvider,
            OpenVisitDayReconciliationStatusProvider>();
        services.TryAddSingleton<
            IFreezeDayReconciliationStatusProvider,
            OpenFreezeDayReconciliationStatusProvider>();
        services.AddScoped<CancelVisitSourcePreparer>();
        services.AddScoped<CancelFreezeSourcePreparer>();
        services.AddScoped<MembershipVisitFreezeSourceReader>();
        services.AddScoped<IMembershipVisitFreezeSourceProvider>(provider =>
            provider.GetRequiredService<MembershipVisitFreezeSourceReader>());
        services.AddScoped<IMembershipVisitFreezeSourceSnapshotProvider>(provider =>
            provider.GetRequiredService<MembershipVisitFreezeSourceReader>());
        services.AddScoped<MembershipFreezeExtensionSourceReader>();
        services.AddScoped<IMembershipExtensionSourceProvider>(provider =>
            provider.GetRequiredService<MembershipFreezeExtensionSourceReader>());
        services.AddScoped<MembershipNonWorkingDayExtensionSourceReader>();
        services.AddScoped<IMembershipExtensionSourceProvider>(provider =>
            provider.GetRequiredService<MembershipNonWorkingDayExtensionSourceReader>());
        services.AddScoped<MembershipNonWorkingDayAffectedScopePreparer>();
        services.AddScoped<IMembershipNonWorkingDayAffectedScopePreparer>(provider =>
            provider.GetRequiredService<MembershipNonWorkingDayAffectedScopePreparer>());
        services.AddScoped<
            IMembershipVisitEligibilityEvaluator,
            MembershipVisitEligibilityEvaluator>();
        services.AddScoped<IMembershipStateRecalculator, MembershipStateRecalculator>();
        services.AddScoped<MembershipVisitEligibilityPreparer>();
        services.AddScoped<MembershipFreezeEligibilityPreparer>();
        services.AddScoped<MembershipExtensionDayWriter>();
        services.AddScoped<MembershipStatePersistenceCoordinator>();
        services.AddScoped<MembershipStateCacheRebuilder>();
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
