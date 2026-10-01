using System.Security.Cryptography;
using Giim.Domain.Assets;
using Giim.Domain.Common;
using Giim.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Giim.Infrastructure.Attachments;

public sealed record AttachmentUpload(string FileName, Stream Content, AttachmentKind Kind, string? Description);

public sealed record AttachmentSummary(Guid Id, string FileName, string ContentType, long SizeBytes, AttachmentKind Kind,
    string? Description, string UploadedBy, DateTimeOffset UploadedAt, string? TicketNumber, bool CanPreview);

/// <summary>
/// Files kept with an asset. Each upload is checked (allowed type, content matches the extension, size), stored under a
/// key GIIM chooses, and recorded with a timeline entry. Removing hides a file but keeps it, so nothing is lost.
/// </summary>
public sealed class AttachmentService(GiimDbContext db, IAttachmentStore store, TimeProvider clock)
{
    private const int SniffBytes = 8192;

    public async Task<IReadOnlyList<AttachmentSummary>> ListAsync(Guid assetId, CancellationToken cancellationToken)
    {
        var files = await db.AssetAttachments.AsNoTracking()
            .Where(a => a.AssetId == assetId && a.RemovedAt == null)
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync(cancellationToken);
        return [.. files.Select(a => new AttachmentSummary(a.Id, a.FileName, a.ContentType, a.SizeBytes, a.Kind, a.Description,
            a.UploadedBy, a.CreatedAt, a.TicketNumber, AttachmentRules.CanPreview(a.ContentType)))];
    }

    public async Task<AssetAttachment> AddAsync(Guid assetId, AttachmentUpload upload, ActionContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(upload);
        ArgumentNullException.ThrowIfNull(context);
        var asset = await db.Assets.FirstOrDefaultAsync(a => a.Id == assetId, cancellationToken)
            ?? throw new KeyNotFoundException("Asset not found.");
        var name = AttachmentRules.CleanFileName(upload.FileName);
        var description = string.IsNullOrWhiteSpace(upload.Description) ? null : upload.Description.Trim();
        if (description?.Length > AssetAttachment.MaxDescriptionLength)
            throw new DomainException($"The description is too long ({AssetAttachment.MaxDescriptionLength} characters maximum).");

        // Read it in (at most 20 MB): the type check needs the start of the file, and the fingerprint all of it.
        using var buffer = new MemoryStream();
        await CopyAtMostAsync(upload.Content, buffer, AttachmentRules.MaxBytes, name, cancellationToken);
        var bytes = buffer.GetBuffer().AsMemory(0, (int)buffer.Length);
        var contentType = AttachmentRules.Check(name, buffer.Length, bytes[..Math.Min(bytes.Length, SniffBytes)]);
        var sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes.Span));

        var existing = await db.AssetAttachments.AsNoTracking()
            .Where(a => a.AssetId == assetId && a.RemovedAt == null).Select(a => a.Sha256).ToListAsync(cancellationToken);
        if (existing.Count >= AssetAttachment.MaxPerAsset)
            throw new DomainException($"An asset can have at most {AssetAttachment.MaxPerAsset} files. Remove some first.");
        if (existing.Contains(sha256))
            throw new DomainException($"{name} is already attached to this asset.");

        var id = Guid.NewGuid();
        var attachment = new AssetAttachment
        {
            Id = id,
            CreatedAt = clock.GetUtcNow(),
            AssetId = assetId,
            FileName = name,
            ContentType = contentType,
            SizeBytes = buffer.Length,
            Kind = upload.Kind,
            Description = description,
            StorageKey = $"assets/{assetId:N}/{id:N}",
            Sha256 = sha256,
            UploadedBy = context.Actor,
            TicketNumber = string.IsNullOrWhiteSpace(context.TicketNumber) ? null : context.TicketNumber.Trim().ToUpperInvariant(),
        };
        // The timeline entry checks who and which ticket before anything is stored.
        var assetEvent = asset.AttachmentAdded(context, attachment);

        buffer.Position = 0;
        await store.SaveAsync(attachment.StorageKey, buffer, contentType, cancellationToken);
        // If this save fails, the stored file is never listed or served: nothing refers to it.
        db.AssetAttachments.Add(attachment);
        db.AssetEvents.Add(assetEvent);
        await db.SaveChangesAsync(cancellationToken);
        return attachment;
    }

    public async Task RemoveAsync(Guid assetId, Guid attachmentId, string reason, ActionContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var attachment = await db.AssetAttachments.FirstOrDefaultAsync(a => a.Id == attachmentId && a.AssetId == assetId, cancellationToken)
            ?? throw new KeyNotFoundException("File not found.");
        var asset = await db.Assets.FirstAsync(a => a.Id == assetId, cancellationToken);

        attachment.Remove(context.Actor, reason, clock.GetUtcNow());
        db.AssetEvents.Add(asset.AttachmentRemoved(context, attachment));
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>A file to download or show; removed files aren't served.</summary>
    public async Task<(AssetAttachment File, Stream Content)> OpenAsync(Guid assetId, Guid attachmentId, CancellationToken cancellationToken)
    {
        var attachment = await db.AssetAttachments.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == attachmentId && a.AssetId == assetId && a.RemovedAt == null, cancellationToken)
            ?? throw new KeyNotFoundException("File not found.");
        var content = await store.OpenAsync(attachment.StorageKey, cancellationToken)
            ?? throw new KeyNotFoundException($"{attachment.FileName} is missing from storage.");
        return (attachment, content);
    }

    private static async Task CopyAtMostAsync(Stream source, Stream target, long limit, string name, CancellationToken cancellationToken)
    {
        var chunk = new byte[81920];
        long total = 0;
        int read;
        while ((read = await source.ReadAsync(chunk, cancellationToken)) > 0)
        {
            total += read;
            if (total > limit)
                throw new DomainException($"{name} is larger than {limit / 1024 / 1024} MB.");
            await target.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
        }
    }
}
