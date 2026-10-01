using System.Text;
using Giim.Domain.Assets;
using Giim.Domain.Common;

namespace Giim.Domain.Tests;

public class AttachmentTests
{
    private static readonly byte[] JpegBytes = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46];
    private static readonly byte[] PngBytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00];
    private static readonly byte[] PdfBytes = Encoding.ASCII.GetBytes("%PDF-1.7\n%âãÏÓ");
    private static readonly byte[] ZipBytes = [0x50, 0x4B, 0x03, 0x04, 0x14, 0x00];
    private static readonly byte[] HeicBytes = [0x00, 0x00, 0x00, 0x18, 0x66, 0x74, 0x79, 0x70, 0x68, 0x65, 0x69, 0x63];
    private static readonly byte[] ExeBytes = [0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00];

    private static string Check(string name, byte[] bytes) => AttachmentRules.Check(name, bytes.Length, bytes);

    [Theory]
    [InlineData("screen.jpg", "image/jpeg")]
    [InlineData("SCREEN.JPEG", "image/jpeg")]
    public void Photos_are_accepted_as_the_type_their_content_shows(string name, string expected)
    {
        Assert.Equal(expected, Check(name, JpegBytes));
        Assert.Equal("image/png", Check("label.png", PngBytes));
        Assert.Equal("image/heic", Check("IMG_0042.HEIC", HeicBytes));
    }

    [Fact]
    public void Documents_are_accepted()
    {
        Assert.Equal("application/pdf", Check("invoice.pdf", PdfBytes));
        Assert.Equal("application/vnd.openxmlformats-officedocument.wordprocessingml.document", Check("report.docx", ZipBytes));
        Assert.Equal("text/plain", Check("notes.txt", Encoding.UTF8.GetBytes("Screen replaced under warranty.")));
    }

    [Theory]
    [InlineData("setup.exe")]
    [InlineData("drawing.svg")]       // can carry scripts
    [InlineData("page.html")]
    [InlineData("macro.docm")]
    [InlineData("README")]
    public void Other_types_are_refused(string name)
    {
        var e = Assert.Throws<DomainException>(() => Check(name, Encoding.UTF8.GetBytes("<svg onload=alert(1)>")));
        Assert.Contains("can't be attached", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_file_renamed_to_look_like_a_photo_or_document_is_refused()
    {
        Assert.Throws<DomainException>(() => Check("photo.jpg", ExeBytes));
        Assert.Throws<DomainException>(() => Check("invoice.pdf", JpegBytes));
        Assert.Throws<DomainException>(() => Check("report.docx", PdfBytes));
        Assert.Throws<DomainException>(() => Check("notes.txt", ExeBytes));   // binary, not text
    }

    [Fact]
    public void Empty_and_oversized_files_are_refused()
    {
        Assert.Throws<DomainException>(() => AttachmentRules.Check("empty.pdf", 0, PdfBytes));
        Assert.Throws<DomainException>(() => AttachmentRules.Check("huge.pdf", AttachmentRules.MaxBytes + 1, PdfBytes));
        Assert.Equal("application/pdf", AttachmentRules.Check("big.pdf", AttachmentRules.MaxBytes, PdfBytes));
    }

    [Fact]
    public void Only_jpg_png_and_webp_are_shown_in_the_page()
    {
        Assert.True(AttachmentRules.CanPreview("image/jpeg"));
        Assert.True(AttachmentRules.CanPreview("image/webp"));
        Assert.False(AttachmentRules.CanPreview("image/heic"));   // most browsers can't show it
        Assert.False(AttachmentRules.CanPreview("application/pdf"));
    }

    [Theory]
    [InlineData(@"C:\fakepath\photo.jpg", "photo.jpg")]
    [InlineData("../../secrets/notes.txt", "notes.txt")]
    [InlineData("in\"voice<1>.pdf", "invoice1.pdf")]
    [InlineData("line\r\nbreak.pdf", "linebreak.pdf")]
    public void File_names_lose_folders_and_unsafe_characters(string given, string expected)
    {
        Assert.Equal(expected, AttachmentRules.CleanFileName(given));
    }

    [Fact]
    public void Long_file_names_are_shortened_keeping_the_extension()
    {
        var name = AttachmentRules.CleanFileName(new string('a', 300) + ".pdf");

        Assert.Equal(AssetAttachment.MaxFileNameLength, name.Length);
        Assert.EndsWith(".pdf", name, StringComparison.Ordinal);
    }

    private static AssetAttachment Invoice() => new()
    {
        FileName = "invoice.pdf", ContentType = "application/pdf", StorageKey = "assets/a/b", Sha256 = new string('0', 64),
        UploadedBy = "jane.tech", Kind = AttachmentKind.Invoice,
    };

    [Fact]
    public void Removing_a_file_needs_a_reason_and_happens_once()
    {
        var file = Invoice();
        var now = new DateTimeOffset(2026, 10, 2, 9, 0, 0, TimeSpan.FromHours(10));

        Assert.Throws<DomainException>(() => file.Remove("jane.tech", " ", now));
        file.Remove("jane.tech", "Wrong asset", now);

        Assert.True(file.IsRemoved);
        Assert.Equal(("jane.tech", "Wrong asset"), (file.RemovedBy, file.RemovedReason));
        Assert.Throws<DomainException>(() => file.Remove("jane.tech", "Again", now));
    }

    [Fact]
    public void Adding_and_removing_files_go_on_the_timeline()
    {
        var asset = new Asset { SerialNumber = "7JK3L92", Manufacturer = "Dell", Model = "Latitude 7450" };
        var file = Invoice();
        var context = new ActionContext("jane.tech", "inc1234");

        var added = asset.AttachmentAdded(context, file);
        file.Remove("jane.tech", "Wrong asset", DateTimeOffset.UtcNow);
        var removed = asset.AttachmentRemoved(context, file);

        Assert.Equal((AssetEventType.AttachmentAdded, "Added invoice: invoice.pdf", "INC1234"), (added.Type, added.Summary, added.TicketNumber));
        Assert.Equal(AssetEventType.AttachmentRemoved, removed.Type);
        Assert.Contains("Wrong asset", removed.DetailsJson, StringComparison.Ordinal);
    }
}
