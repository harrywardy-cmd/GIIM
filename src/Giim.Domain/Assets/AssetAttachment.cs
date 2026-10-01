using Giim.Domain.Common;

namespace Giim.Domain.Assets;

public enum AttachmentKind { Photo, Invoice, Warranty, DisposalCertificate, RepairReport, Other }

/// <summary>
/// A file kept with an asset: a photo of damage, the invoice, a warranty document, a disposal certificate. The file
/// itself is in storage under <see cref="StorageKey"/>; this is the record of it. Removing hides it but keeps the file
/// and the record, so the history stays complete.
/// </summary>
public sealed class AssetAttachment : Entity
{
    public const int MaxFileNameLength = 200;
    public const int MaxDescriptionLength = 500;
    public const int MaxPerAsset = 50;

    public Guid AssetId { get; init; }
    public required string FileName { get; init; }
    public required string ContentType { get; init; }
    public long SizeBytes { get; init; }
    public AttachmentKind Kind { get; init; }
    public string? Description { get; init; }

    /// <summary>Where the file is kept (never the user's file name).</summary>
    public required string StorageKey { get; init; }

    /// <summary>SHA-256 of the content, hex: spots the same file attached twice and proves it hasn't changed.</summary>
    public required string Sha256 { get; init; }

    public required string UploadedBy { get; init; }
    public string? TicketNumber { get; init; }

    public DateTimeOffset? RemovedAt { get; private set; }
    public string? RemovedBy { get; private set; }
    public string? RemovedReason { get; private set; }
    public bool IsRemoved => RemovedAt is not null;

    public void Remove(string actor, string reason, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(actor))
            throw new DomainException("Every action must record who performed it.");
        if (IsRemoved)
            throw new DomainException($"{FileName} has already been removed.");
        RemovedReason = string.IsNullOrWhiteSpace(reason)
            ? throw new DomainException("Say why the file is being removed.")
            : reason.Trim();
        RemovedBy = actor;
        RemovedAt = now;
        UpdatedAt = now;
    }

    public static string KindText(AttachmentKind kind) => kind switch
    {
        AttachmentKind.Photo => "photo",
        AttachmentKind.Invoice => "invoice",
        AttachmentKind.Warranty => "warranty document",
        AttachmentKind.DisposalCertificate => "disposal certificate",
        AttachmentKind.RepairReport => "repair report",
        _ => "file",
    };
}

/// <summary>
/// Which files can be attached. An allow-list by extension, and the content must look like what the extension says,
/// so a renamed program or web page is refused. Web pages and SVG images (which can carry scripts) are never accepted.
/// </summary>
public static class AttachmentRules
{
    public const long MaxBytes = 20 * 1024 * 1024;

    private sealed record FileType(string ContentType, bool Preview, Func<ReadOnlyMemory<byte>, bool> Looks);

    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF];
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] Pdf = "%PDF-"u8.ToArray();
    private static readonly byte[] Zip = [0x50, 0x4B, 0x03, 0x04];
    private static readonly byte[] Ole = [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];

    private static bool StartsWith(ReadOnlyMemory<byte> data, byte[] prefix) => data.Span.StartsWith(prefix);
    private static bool Riff(ReadOnlyMemory<byte> d, string type) =>
        d.Length >= 12 && d.Span[..4].SequenceEqual("RIFF"u8) && d.Span[8..12].SequenceEqual(System.Text.Encoding.ASCII.GetBytes(type));
    private static bool Iso(ReadOnlyMemory<byte> d) => d.Length >= 12 && d.Span[4..8].SequenceEqual("ftyp"u8);
    /// <summary>Text files: no NUL bytes in the first part, so it isn't a binary renamed to .txt.</summary>
    private static bool Text(ReadOnlyMemory<byte> d) => !d.Span[..Math.Min(d.Length, 8192)].Contains((byte)0);

    private static readonly Dictionary<string, FileType> Types = new(StringComparer.OrdinalIgnoreCase)
    {
        [".jpg"] = new("image/jpeg", true, d => StartsWith(d, Jpeg)),
        [".jpeg"] = new("image/jpeg", true, d => StartsWith(d, Jpeg)),
        [".png"] = new("image/png", true, d => StartsWith(d, Png)),
        [".webp"] = new("image/webp", true, d => Riff(d, "WEBP")),
        [".heic"] = new("image/heic", false, Iso),
        [".heif"] = new("image/heif", false, Iso),
        [".pdf"] = new("application/pdf", false, d => StartsWith(d, Pdf)),
        [".docx"] = new("application/vnd.openxmlformats-officedocument.wordprocessingml.document", false, d => StartsWith(d, Zip)),
        [".xlsx"] = new("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", false, d => StartsWith(d, Zip)),
        [".msg"] = new("application/vnd.ms-outlook", false, d => StartsWith(d, Ole)),
        [".eml"] = new("message/rfc822", false, Text),
        [".txt"] = new("text/plain", false, Text),
        [".csv"] = new("text/csv", false, Text),
    };

    public static IReadOnlyCollection<string> Extensions => Types.Keys;

    /// <summary>Checks the file and returns the content type GIIM will serve it as (never the browser's claim).</summary>
    public static string Check(string fileName, long length, ReadOnlyMemory<byte> start)
    {
        var extension = Path.GetExtension(fileName);
        if (!Types.TryGetValue(extension, out var type))
            throw new DomainException($"{(extension.Length == 0 ? "Files without an extension" : $"{extension} files")} can't be attached. "
                + "Use a photo (JPG, PNG, HEIC), PDF, Word, Excel, Outlook message or text file.");
        if (length == 0)
            throw new DomainException($"{fileName} is empty.");
        if (length > MaxBytes)
            throw new DomainException($"{fileName} is larger than {MaxBytes / 1024 / 1024} MB.");
        if (!type.Looks(start))
            throw new DomainException($"{fileName} isn't really a {extension.TrimStart('.').ToUpperInvariant()} file.");
        return type.ContentType;
    }

    /// <summary>Whether a browser can show it in the page (JPG, PNG, WebP); everything else is downloaded.</summary>
    public static bool CanPreview(string contentType) => Types.Values.Any(t => t.Preview && t.ContentType == contentType);

    /// <summary>The name as shown and downloaded: no folders, no control characters, not too long.</summary>
    public static string CleanFileName(string fileName)
    {
        var name = Path.GetFileName(fileName.Replace('\\', '/'));
        name = new string([.. name.Where(c => !char.IsControl(c) && c is not ('"' or '<' or '>' or '|' or ':' or '*' or '?'))]).Trim();
        if (name.Length == 0) throw new DomainException("The file has no name.");
        if (name.Length <= AssetAttachment.MaxFileNameLength) return name;
        var extension = Path.GetExtension(name);
        return name[..(AssetAttachment.MaxFileNameLength - extension.Length)] + extension;
    }
}
