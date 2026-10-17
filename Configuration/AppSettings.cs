using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LanShare.Configuration;

public sealed class AppSettings
{
    public int Port { get; set; } = 8080;
    public string DataDirectory { get; set; } = "data";
    public long MaxFileSizeMB { get; set; } = 2048;
    public bool OpenBrowser { get; set; } = true;
    public string AccessPassword { get; set; } = "";
    public string AdminPassword { get; set; } = "";
    [JsonIgnore] public string ConfigPath { get; private set; } = "";
    [JsonIgnore] public long MaxFileSizeBytes => checked(MaxFileSizeMB * 1024L * 1024L);

    public static AppSettings Load(string baseDirectory)
    {
        var path = Path.Combine(baseDirectory, "lanshare.json");
        var settings = File.Exists(path)
            ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), StorageJson.Options)
                ?? throw new InvalidOperationException("配置文件不能为空。")
            : new AppSettings();
        var save = !File.Exists(path) || string.IsNullOrWhiteSpace(settings.AdminPassword);
        if (string.IsNullOrWhiteSpace(settings.AdminPassword))
            settings.AdminPassword = Convert.ToHexString(RandomNumberGenerator.GetBytes(12)).ToLowerInvariant();
        if (save) File.WriteAllText(path, JsonSerializer.Serialize(settings, StorageJson.Options));
        // Environment overrides do not change the saved configuration.
        settings.Port = int.TryParse(Environment.GetEnvironmentVariable("LANSHARE_PORT"), out var port) ? port : settings.Port;
        settings.DataDirectory = Environment.GetEnvironmentVariable("LANSHARE_DATA_DIR") ?? settings.DataDirectory;
        settings.AccessPassword = Environment.GetEnvironmentVariable("LANSHARE_ACCESS_PASSWORD") ?? settings.AccessPassword;
        settings.AdminPassword = Environment.GetEnvironmentVariable("LANSHARE_ADMIN_PASSWORD") ?? settings.AdminPassword;
        if (settings.Port is < 1 or > 65535 || settings.MaxFileSizeMB is < 1 or > 1048576)
            throw new InvalidOperationException("端口或最大文件大小配置无效。");
        if (string.IsNullOrWhiteSpace(settings.AdminPassword)) throw new InvalidOperationException("管理员密码不能为空。");
        settings.DataDirectory = Path.GetFullPath(settings.DataDirectory, baseDirectory);
        settings.ConfigPath = path;
        return settings;
    }
}

internal static class StorageJson
{
    internal static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = true };
}
