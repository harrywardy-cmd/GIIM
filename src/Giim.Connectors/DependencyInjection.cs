using Azure.Core;
using Azure.Identity;
using Giim.Connectors.CloudAccounts;
using Giim.Connectors.Email;
using Giim.Connectors.Intune;
using Giim.Connectors.People;
using Giim.Connectors.ServiceDesk;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Giim.Connectors;

public static class DependencyInjection
{
    public static IServiceCollection AddGiimConnectors(this IServiceCollection services, IConfiguration configuration)
    {
        var intune = configuration.GetSection(IntuneOptions.SectionName);
        services.Configure<IntuneOptions>(intune);

        // One Graph credential for everything that calls Microsoft Graph (Intune, email, staff directory).
        services.AddSingleton<TokenCredential>(sp =>
        {
            var o = sp.GetRequiredService<IOptions<IntuneOptions>>().Value;
            return !string.IsNullOrEmpty(o.TenantId) && !string.IsNullOrEmpty(o.ClientId) && !string.IsNullOrEmpty(o.ClientSecret)
                ? new ClientSecretCredential(o.TenantId, o.ClientId, o.ClientSecret)
                : new DefaultAzureCredential();
        });

        if (intune.GetValue<IntuneSource?>(nameof(IntuneOptions.Source)) == IntuneSource.Graph)
        {
            services.AddHttpClient<IIntuneClient, GraphIntuneClient>(client => client.Timeout = TimeSpan.FromMinutes(2));
        }
        else
        {
            services.AddSingleton<IIntuneClient, FileIntuneClient>();
        }

        var email = configuration.GetSection(EmailOptions.SectionName);
        services.Configure<EmailOptions>(email);
        services.TryAddSingleton(TimeProvider.System);
        switch (email.GetValue<EmailMode?>(nameof(EmailOptions.Mode)) ?? EmailMode.None)
        {
            case EmailMode.Graph:
                services.AddHttpClient<IEmailSender, GraphEmailSender>(client => client.Timeout = TimeSpan.FromSeconds(30));
                break;
            case EmailMode.File:
                services.AddSingleton<IEmailSender, FileEmailSender>();
                break;
        }

        var serviceDesk = configuration.GetSection(ServiceDeskOptions.SectionName);
        services.Configure<ServiceDeskOptions>(serviceDesk);
        switch (serviceDesk.GetValue<ServiceDeskMode?>(nameof(ServiceDeskOptions.Mode)) ?? ServiceDeskMode.None)
        {
            case ServiceDeskMode.Api:
                services.AddHttpClient(nameof(ZohoTokenProvider), client => client.Timeout = TimeSpan.FromSeconds(30));
                services.AddSingleton<ZohoTokenProvider>();
                services.AddHttpClient<IServiceDeskClient, ApiServiceDeskClient>(client => client.Timeout = TimeSpan.FromSeconds(60));
                break;
            case ServiceDeskMode.File:
                services.AddSingleton<IServiceDeskClient, FileServiceDeskClient>();
                break;
        }

        // Staff directory: Entra ID in Azure (read-only, through Graph), a CSV file on a developer PC.
        var people = configuration.GetSection(PeopleOptions.SectionName);
        services.Configure<PeopleOptions>(people);
        if (people.GetValue<PeopleSource?>(nameof(PeopleOptions.Source)) == PeopleSource.Entra)
            services.AddHttpClient<IPeopleSource, EntraPeopleSource>(client => client.Timeout = TimeSpan.FromMinutes(2));
        else
            services.AddSingleton<IPeopleSource, FilePeopleSource>();

        // Whether a new account has reached Entra ID yet (automation): Graph in Azure, the stand-in agent's file locally.
        var cloud = configuration.GetSection(CloudDirectoryOptions.SectionName);
        services.Configure<CloudDirectoryOptions>(cloud);
        if (cloud.GetValue<CloudDirectorySource?>(nameof(CloudDirectoryOptions.Source)) == CloudDirectorySource.Graph)
            services.AddHttpClient<ICloudDirectory, GraphCloudDirectory>(client => client.Timeout = TimeSpan.FromSeconds(30));
        else
            services.AddSingleton<ICloudDirectory, FileCloudDirectory>();

        return services;
    }
}
