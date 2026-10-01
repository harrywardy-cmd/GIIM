using System.Text;
using Giim.Infrastructure.Attachments;
using Microsoft.Extensions.Options;

namespace Giim.Infrastructure.Tests;

public sealed class FileAttachmentStoreTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "giim-attachments-" + Guid.NewGuid().ToString("N"));

    private FileAttachmentStore Store() => new(Options.Create(new AttachmentOptions { FilePath = _folder }));

    [Fact]
    public async Task Saves_and_reads_back_a_file()
    {
        var store = Store();
        await store.SaveAsync("assets/a1/f1", new MemoryStream(Encoding.UTF8.GetBytes("hello")), "text/plain", CancellationToken.None);

        await using var content = await store.OpenAsync("assets/a1/f1", CancellationToken.None);
        using var reader = new StreamReader(content!);
        Assert.Equal("hello", await reader.ReadToEndAsync());
        Assert.Null(await store.OpenAsync("assets/a1/missing", CancellationToken.None));
    }

    [Fact]
    public async Task Never_overwrites_a_stored_file()
    {
        var store = Store();
        await store.SaveAsync("assets/a1/f1", new MemoryStream([1]), "text/plain", CancellationToken.None);

        await Assert.ThrowsAsync<IOException>(() => store.SaveAsync("assets/a1/f1", new MemoryStream([2]), "text/plain", CancellationToken.None));
    }

    [Theory]
    [InlineData("../outside")]
    [InlineData("assets/../../outside")]
    public async Task A_key_can_never_point_outside_the_folder(string key)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Store().SaveAsync(key, new MemoryStream([1]), "text/plain", CancellationToken.None));
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }
}
