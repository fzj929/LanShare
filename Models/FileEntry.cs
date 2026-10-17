namespace LanShare.Models;

public sealed record FileEntry(string Id, string Name, long Size, DateTimeOffset UploadedAt);
