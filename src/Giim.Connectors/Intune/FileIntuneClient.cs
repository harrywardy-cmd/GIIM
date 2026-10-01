using System.Runtime.CompilerServices;
using System.Text.Json;
using Giim.Connectors.Graph;
using Microsoft.Extensions.Options;

namespace Giim.Connectors.Intune;

/// <summary>Reads a Graph-shaped export ({ "value": [...] }) from disk, e.g. samples/intune-devices.json.</summary>
public sealed class FileIntuneClient(IOptions<IntuneOptions> options) : IIntuneClient
{
    public async IAsyncEnumerable<IntuneDevice> GetManagedDevicesAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var path = SamplePath.Resolve(options.Value.FilePath);
        await using var stream = File.OpenRead(path);
        var page = await JsonSerializer.DeserializeAsync<GraphPage<IntuneDevice>>(stream, JsonSerializerOptions.Web, cancellationToken)
            ?? throw new InvalidDataException($"{path} is empty.");

        foreach (var device in page.Value)
            yield return device;
    }
}
