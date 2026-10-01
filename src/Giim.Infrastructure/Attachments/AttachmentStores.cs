using Azure;
using Azure.Core;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Options;

namespace Giim.Infrastructure.Attachments;

public enum AttachmentStoreMode
{
    /// <summary>A folder on disk (artifacts/attachments). For a developer PC.</summary>
    File,

    /// <summary>The private "attachments" container in Azure Blob Storage, as the app's managed identity.</summary>
    Blob,
}

public sealed class AttachmentOptions
{
    public const string SectionName = "Attachments";

    public AttachmentStoreMode Mode { get; set; } = AttachmentStoreMode.File;

    /// <summary>File mode: the folder, relative to the repository root unless rooted.</summary>
    public string FilePath { get; set; } = "artifacts/attachments";

    /// <summary>Blob mode: the container's address, e.g. https://stgiimprod.blob.core.windows.net/attachments (set by the deployment).</summary>
    public Uri? ContainerUri { get; set; }
}

/// <summary>Keeps attachment files. Write-once: a key is never overwritten, and files are never deleted by GIIM.</summary>
public interface IAttachmentStore
{
    Task SaveAsync(string key, Stream content, string contentType, CancellationToken cancellationToken);

    /// <summary>The file's content, or null if it isn't there.</summary>
    Task<Stream?> OpenAsync(string key, CancellationToken cancellationToken);
}

public sealed class FileAttachmentStore(IOptions<AttachmentOptions> options) : IAttachmentStore
{
    private readonly string _root = Root(options.Value.FilePath);

    public async Task SaveAsync(string key, Stream content, string contentType, CancellationToken cancellationToken)
    {
        var path = PathFor(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
        await content.CopyToAsync(file, cancellationToken);
    }

    public Task<Stream?> OpenAsync(string key, CancellationToken cancellationToken)
    {
        var path = PathFor(key);
        return Task.FromResult<Stream?>(File.Exists(path) ? File.OpenRead(path) : null);
    }

    /// <summary>Keys are GIIM's own (assets/{id}/{id}), but check anyway that one can never point outside the folder.</summary>
    private string PathFor(string key)
    {
        var path = Path.GetFullPath(Path.Combine(_root, key));
        if (!path.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new ArgumentException("Invalid attachment key.", nameof(key));
        return path;
    }

    private static string Root(string path)
    {
        if (Path.IsPathRooted(path)) return Path.GetFullPath(path);
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "Giim.slnx")))
                return Path.GetFullPath(Path.Combine(dir.FullName, path));
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, path));
    }
}

public sealed class BlobAttachmentStore(IOptions<AttachmentOptions> options, TokenCredential credential) : IAttachmentStore
{
    private readonly BlobContainerClient _container = new(
        options.Value.ContainerUri ?? throw new InvalidOperationException("Attachments:ContainerUri is not set."), credential);

    public async Task SaveAsync(string key, Stream content, string contentType, CancellationToken cancellationToken)
    {
        // IfNoneMatch *: fails rather than overwrite, so a stored file can't be replaced.
        await _container.GetBlobClient(key).UploadAsync(content, new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders { ContentType = contentType },
            Conditions = new BlobRequestConditions { IfNoneMatch = ETag.All },
        }, cancellationToken);
    }

    public async Task<Stream?> OpenAsync(string key, CancellationToken cancellationToken)
    {
        try
        {
            return await _container.GetBlobClient(key).OpenReadAsync(cancellationToken: cancellationToken);
        }
        catch (RequestFailedException e) when (e.Status == 404)
        {
            return null;
        }
    }
}
