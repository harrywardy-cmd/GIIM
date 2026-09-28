using Azure.Core;
using Azure.Identity;
using Giim.Connectors.Intune;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Giim.Connectors;

public static class DependencyInjection
{
    public static IServiceCollection AddGiimConnectors(this IServiceCollection services, IConfiguration configuration)
    {
        var intune = configuration.GetSection(IntuneOptions.SectionName);
        services.Configure<IntuneOptions>(intune);

        if (intune.GetValue<IntuneSource?>(nameof(IntuneOptions.Source)) == IntuneSource.Graph)
        {
            services.AddSingleton<TokenCredential>(sp =>
            {
                var o = sp.GetRequiredService<IOptions<IntuneOptions>>().Value;
                return !string.IsNullOrEmpty(o.TenantId) && !string.IsNullOrEmpty(o.ClientId) && !string.IsNullOrEmpty(o.ClientSecret)
                    ? new ClientSecretCredential(o.TenantId, o.ClientId, o.ClientSecret)
                    : new DefaultAzureCredential();
            });
            services.AddHttpClient<IIntuneClient, GraphIntuneClient>(client => client.Timeout = TimeSpan.FromMinutes(2));
        }
        else
        {
            services.AddSingleton<IIntuneClient, FileIntuneClient>();
        }

        return services;
    }
}
