using System.Formats.Tar;
using System.Text;

namespace HQ.Plugins.FileStorage;

internal static class TarHelper
{
    public static MemoryStream CreateTarWithFile(string fileName, byte[] content)
    {
        var stream = new MemoryStream();
        using (var writer = new TarWriter(stream, leaveOpen: true))
        {
            var entry = new PaxTarEntry(TarEntryType.RegularFile, fileName)
            {
                DataStream = new MemoryStream(content)
            };
            writer.WriteEntry(entry);
        }
        stream.Position = 0;
        return stream;
    }

    public static MemoryStream CreateTarWithFile(string fileName, string text)
    {
        return CreateTarWithFile(fileName, Encoding.UTF8.GetBytes(text));
    }

    public static async Task<(string FileName, byte[] Content)> ExtractFirstFileAsync(Stream tarStream)
    {
        var reader = new TarReader(tarStream);
        var entry = await reader.GetNextEntryAsync();
        if (entry == null)
            throw new InvalidOperationException("Tar archive is empty");

        if (entry.DataStream == null)
            return (entry.Name, Array.Empty<byte>());

        using var ms = new MemoryStream();
        await entry.DataStream.CopyToAsync(ms);
        return (entry.Name, ms.ToArray());
    }
}
