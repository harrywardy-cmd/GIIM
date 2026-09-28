namespace Giim.Connectors;

/// <summary>Resolves relative paths from the repository root, so the API, workers and tests find the same sample files.</summary>
internal static class SamplePath
{
    public static string Resolve(string path)
    {
        if (Path.IsPathRooted(path)) return path;

        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, path);
            if (File.Exists(candidate)) return candidate;
        }

        throw new FileNotFoundException($"'{path}' not found from {AppContext.BaseDirectory} upwards.");
    }
}
