using Giim.Agent;
using Giim.Agent.Accounts;
using Microsoft.Extensions.Options;

// The GIIM on-prem agent. Runs as a Windows service on a domain-joined server (sc.exe create, see
// docs/onprem-agent.md), or in a console window on a developer PC with the stand-in directory.
var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService(o => o.ServiceName = "GIIM Agent");

builder.Services.Configure<AgentOptions>(builder.Configuration.GetSection(AgentOptions.SectionName));
builder.Services.Configure<AccountRules>(builder.Configuration.GetSection(AccountRules.SectionName));
builder.Services.AddSingleton(TimeProvider.System);

var agent = builder.Configuration.GetSection(AgentOptions.SectionName).Get<AgentOptions>() ?? new AgentOptions();
switch (agent.Directory)
{
    case DirectoryKind.StandIn:
        builder.Services.AddSingleton<IDirectory, StandInDirectory>();
        break;
    default:
        throw new InvalidOperationException(
            "Agent:Directory 'ActiveDirectory' isn't built yet: it needs the AD administrators' decisions first (docs/onprem-agent.md). Use 'StandIn'.");
}
if (agent.Auth == AgentAuth.Key && string.IsNullOrWhiteSpace(agent.Key))
    throw new InvalidOperationException("Set Agent:Key (the same key as GIIM's Agent:Key), or use Agent:Auth 'Entra'.");

builder.Services.AddTransient<GiimAuthHandler>();
builder.Services.AddHttpClient<GiimClient>((sp, http) =>
    {
        var url = sp.GetRequiredService<IOptions<AgentOptions>>().Value.GiimUrl.ToString();
        http.BaseAddress = new Uri(url.EndsWith('/') ? url : url + "/");
        http.Timeout = TimeSpan.FromSeconds(60);
    })
    .AddHttpMessageHandler<GiimAuthHandler>();
builder.Services.AddSingleton<JobRunner>();
builder.Services.AddHostedService<AgentWorker>();

builder.Build().Run();
