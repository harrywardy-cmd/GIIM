using Giim.Infrastructure.Activity;
using Giim.Infrastructure.Assets;
using Giim.Infrastructure.Attachments;
using Giim.Infrastructure.Cases;
using Giim.Infrastructure.Devices;
using Giim.Infrastructure.Importing;
using Giim.Infrastructure.Locations;
using Giim.Infrastructure.Notifications;
using Giim.Infrastructure.People;
using Giim.Infrastructure.Persistence;
using Giim.Infrastructure.Provisioning;
using Giim.Infrastructure.Reports;
using Giim.Infrastructure.Requests;
using Giim.Infrastructure.Stock;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Giim.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddGiimInfrastructure(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<GiimDbContext>(options =>
            options.UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure()));
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<AssetImportService>();
        services.AddScoped<AssetLifecycleService>();
        services.AddScoped<AssignmentService>();
        services.AddScoped<RepairService>();
        services.AddScoped<LocationService>();
        services.AddScoped<ActivityService>();
        services.AddScoped<ReportService>();
        services.AddScoped<RequestService>();
        services.AddScoped<ProfileService>();
        services.AddScoped<CaseService>();
        services.AddScoped<ReminderService>();
        services.AddScoped<PeopleSyncService>();
        services.AddScoped<LegacyOwnerService>();
        services.AddScoped<StockService>();
        services.AddScoped<IntuneSyncService>();
        services.AddScoped<ReconciliationService>();

        return services;
    }

    /// <summary>Asset files: a local folder on a developer PC, the private Blob Storage container in Azure.</summary>
    public static IServiceCollection AddGiimAttachments(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(AttachmentOptions.SectionName);
        services.Configure<AttachmentOptions>(section);
        if (section.GetValue<AttachmentStoreMode?>(nameof(AttachmentOptions.Mode)) == AttachmentStoreMode.Blob)
            services.AddSingleton<IAttachmentStore, BlobAttachmentStore>();
        else
            services.AddSingleton<IAttachmentStore, FileAttachmentStore>();
        services.AddScoped<AttachmentService>();
        return services;
    }
}
