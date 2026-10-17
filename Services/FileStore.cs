using System.Text.Json;
using LanShare.Configuration;
using LanShare.Models;

namespace LanShare.Services;

public sealed class FileStore : IDisposable
{
    private readonly string filesDirectory;
    public string StagingDirectory { get; }
    public SemaphoreSlim UploadSlots { get; } = new(4);

    public FileStore(AppSettings settings)
    {
        filesDirectory = Path.Combine(settings.DataDirectory, "files");
        StagingDirectory = Path.Combine(settings.DataDirectory, "staging");
        Directory.CreateDirectory(filesDirectory);
        Directory.CreateDirectory(StagingDirectory);
    }

    public FileEntry[] List() => Directory.EnumerateDirectories(filesDirectory)
        .Select(d => Find(Path.GetFileName(d))).OfType<FileEntry>()
        .OrderByDescending(x => x.UploadedAt).ToArray();

    public FileEntry? Find(string id)
    {
        if (!Guid.TryParseExact(id, "N", out _)) return null;
        try
        {
            if (!File.Exists(ContentPath(id))) return null;
            return JsonSerializer.Deserialize<FileEntry>(File.ReadAllText(Path.Combine(filesDirectory, id, "metadata.json")), StorageJson.Options);
        }
        catch (IOException) { return null; }
        catch (JsonException) { return null; }
    }

    public string ContentPath(string id) => Path.Combine(filesDirectory, id, "content.bin");
    public async Task CommitAsync(string stagingPath, FileEntry entry, CancellationToken cancellationToken)
    {
        await File.WriteAllTextAsync(Path.Combine(stagingPath, "metadata.json"),
            JsonSerializer.Serialize(entry, StorageJson.Options), cancellationToken);
        Directory.Move(stagingPath, Path.Combine(filesDirectory, entry.Id));
    }
    public void Delete(string id) => Directory.Delete(Path.Combine(filesDirectory, id), true);
    public void Dispose() => UploadSlots.Dispose();
}
