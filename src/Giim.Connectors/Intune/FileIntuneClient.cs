using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Giim.Connectors.Intune;

/// <summary>Reads a Graph-shaped export ({ "value": [...] }) from disk, e.g. samples/intune-devices.json.</summary>
public sealed class FileIntuneClient(IOptions<IntuneOptions> options) : IIntuneClient
{
    public async IAsyncEnumerable<IntuneDevice> GetManagedDevicesAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var path = ResolvePath(options.Value.FilePath);
        await using var stream = File.OpenRead(path);
        var page = await JsonSerializer.DeserializeAsync<GraphPage>(stream, JsonSerializerOptions.Web, cancellationToken)
            ?? throw new InvalidDataException($"{path} is empty.");

        foreach (var device in page.Value)
            yield return device;
    }

    /// <summary>Relative paths are resolved from the repository root so the API and workers find the same file.</summary>
    private static string ResolvePath(string path)
    {
        if (Path.IsPathRooted(path)) return path;

        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, path);
            if (File.Exists(candidate)) return candidate;
        }

        throw new FileNotFoundException($"Intune export '{path}' not found from {AppContext.BaseDirectory} upwards.");
    }
}
