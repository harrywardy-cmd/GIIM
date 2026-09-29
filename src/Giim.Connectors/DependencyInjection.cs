using Azure.Core;
using Azure.Identity;
using Giim.Connectors.Email;
using Giim.Connectors.Intune;
using Giim.Connectors.People;
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

        // One Graph credential for everything that calls Microsoft Graph (Intune, email).
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

        // Only the file source exists today; the AD source (via the on-prem agent) is added here later.
        services.Configure<PeopleOptions>(configuration.GetSection(PeopleOptions.SectionName));
        services.AddSingleton<IPeopleSource, FilePeopleSource>();

        return services;
    }
}
