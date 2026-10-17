using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Features;

var baseDir = AppContext.BaseDirectory;
var json = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };
var settingsPath = Path.Combine(baseDir, "lanshare.json");
Settings settings;
try
{
    settings = File.Exists(settingsPath)
        ? JsonSerializer.Deserialize<Settings>(File.ReadAllText(settingsPath), json) ?? new()
        : new();
    if (string.IsNullOrWhiteSpace(settings.AdminPassword))
        settings.AdminPassword = Convert.ToHexString(RandomNumberGenerator.GetBytes(12)).ToLowerInvariant();
    // Runtime overrides do not modify the saved configuration.
    if (!File.Exists(settingsPath)) File.WriteAllText(settingsPath, JsonSerializer.Serialize(settings, json));
    else if (string.IsNullOrWhiteSpace(JsonSerializer.Deserialize<Settings>(File.ReadAllText(settingsPath), json)?.AdminPassword))
        File.WriteAllText(settingsPath, JsonSerializer.Serialize(settings, json));
    settings.Port = int.TryParse(Environment.GetEnvironmentVariable("LANSHARE_PORT"), out var port) ? port : settings.Port;
    settings.DataDirectory = Environment.GetEnvironmentVariable("LANSHARE_DATA_DIR") ?? settings.DataDirectory;
    settings.AccessPassword = Environment.GetEnvironmentVariable("LANSHARE_ACCESS_PASSWORD") ?? settings.AccessPassword;
    settings.AdminPassword = Environment.GetEnvironmentVariable("LANSHARE_ADMIN_PASSWORD") ?? settings.AdminPassword;
    if (settings.Port is < 1 or > 65535 || settings.MaxFileSizeMB is < 1 or > 1048576)
        throw new InvalidOperationException("端口或最大文件大小配置无效。");
    if (string.IsNullOrWhiteSpace(settings.AdminPassword)) throw new InvalidOperationException("管理员密码不能为空。");
}
catch (Exception ex)
{
    Console.Error.WriteLine($"无法读取或创建配置 {settingsPath}\n{ex.Message}\n请将程序放在有写入权限的文件夹中。");
    return 1;
}

var dataDir = Path.GetFullPath(settings.DataDirectory, baseDir);
var filesDir = Path.Combine(dataDir, "files");
var stagingDir = Path.Combine(dataDir, "staging");
try { Directory.CreateDirectory(filesDir); Directory.CreateDirectory(stagingDir); }
catch (Exception ex) { Console.Error.WriteLine($"无法创建文件存储目录：{ex.Message}"); return 1; }
var maxBytes = checked(settings.MaxFileSizeMB * 1024L * 1024L);
var sessions = new ConcurrentDictionary<string, Session>();
var loginAttempts = new ConcurrentDictionary<string, Attempt>();
using var uploadSlots = new SemaphoreSlim(4);
var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { Args = args, ContentRootPath = baseDir });
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(o => o.SingleLine = true);
builder.Logging.SetMinimumLevel(LogLevel.Warning);
builder.WebHost.ConfigureKestrel(o =>
{
    o.ListenAnyIP(settings.Port);
    o.Limits.MaxRequestBodySize = maxBytes;
    o.AddServerHeader = false;
});
var app = builder.Build();
bool IsAdmin(HttpContext c) => GetSession(c)?.Admin == true;
Session? GetSession(HttpContext c)
{
    if (!c.Request.Cookies.TryGetValue("lanshare-session", out var key)) return null;
    if (!sessions.TryGetValue(key, out var s)) return null;
    if (s.Expires > DateTimeOffset.UtcNow) return s;
    sessions.TryRemove(key, out _);
    return null;
}
bool Authorized(HttpContext c) => string.IsNullOrEmpty(settings.AccessPassword) || GetSession(c) is not null;
bool PasswordMatches(string a, string b) => CryptographicOperations.FixedTimeEquals(
    SHA256.HashData(Encoding.UTF8.GetBytes(a)), SHA256.HashData(Encoding.UTF8.GetBytes(b)));
bool ValidId(string id) => Guid.TryParseExact(id, "N", out _);
FileEntry? Find(string id)
{
    if (!ValidId(id)) return null;
    var folder = Path.Combine(filesDir, id);
    try
    {
        if (!File.Exists(Path.Combine(folder, "content.bin"))) return null;
        return JsonSerializer.Deserialize<FileEntry>(File.ReadAllText(Path.Combine(folder, "metadata.json")), json);
    }
    catch (IOException) { return null; }
    catch (JsonException) { return null; }
}

app.Use(async (c, next) =>
{
    c.Response.Headers["X-Content-Type-Options"] = "nosniff";
    c.Response.Headers["X-Frame-Options"] = "DENY";
    c.Response.Headers["Referrer-Policy"] = "no-referrer";
    c.Response.Headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; connect-src 'self'; frame-ancestors 'none'; base-uri 'none'; form-action 'self'";
    c.Response.Headers.CacheControl = "no-store";
    // IP/localhost hostnames prevent browser DNS-rebinding access to a private file station.
    var host = c.Request.Host.Host.Trim('[', ']');
    if (!IPAddress.TryParse(host, out _) && !host.Equals("localhost", StringComparison.OrdinalIgnoreCase) &&
        !host.Equals(Dns.GetHostName(), StringComparison.OrdinalIgnoreCase))
    { c.Response.StatusCode = 400; return; }
    if (c.Request.Method is not ("GET" or "HEAD"))
    {
        var expectedOrigin = $"{c.Request.Scheme}://{c.Request.Host}";
        var origin = c.Request.Headers.Origin.ToString();
        if (c.Request.Headers["X-LanShare-Request"] != "1" ||
            (!string.IsNullOrEmpty(origin) && !origin.Equals(expectedOrigin, StringComparison.OrdinalIgnoreCase)))
        { c.Response.StatusCode = 403; await c.Response.WriteAsJsonAsync(new { error = "请求来源无效，请刷新页面后重试。" }); return; }
    }
    if (c.Request.Path.StartsWithSegments("/api") && c.Request.Path != "/api/status" &&
        c.Request.Path != "/api/login" && c.Request.Path != "/api/logout" && !Authorized(c))
    { c.Response.StatusCode = 401; await c.Response.WriteAsJsonAsync(new { error = "请输入访问密码。" }); return; }
    try { await next(c); }
    catch (BadHttpRequestException ex) when (!c.Response.HasStarted)
    { c.Response.StatusCode = ex.StatusCode; await c.Response.WriteAsJsonAsync(new { error = ex.StatusCode == 413 ? "文件超过大小限制。" : "上传请求无效。" }); }
    catch (OperationCanceledException) when (c.RequestAborted.IsCancellationRequested) { }
    catch (IOException ex) when (!c.Response.HasStarted)
    { app.Logger.LogWarning(ex, "文件操作失败"); c.Response.StatusCode = 500; await c.Response.WriteAsJsonAsync(new { error = "文件操作失败，请检查磁盘空间、文件权限或稍后重试。" }); }
});

foreach (var (route, resource, contentType) in new[] {
    ("/", "index.html", "text/html; charset=utf-8"),
    ("/app.js", "app.js", "text/javascript; charset=utf-8"),
    ("/app.css", "app.css", "text/css; charset=utf-8") })
{
    var bytes = ReadResource(resource);
    app.MapGet(route, () => Results.Bytes(bytes, contentType));
}
app.MapGet("/api/status", (HttpContext c) => Results.Ok(new
{
    name = "局域网文件站", version = "1.0.0", requiresPassword = !string.IsNullOrEmpty(settings.AccessPassword),
    authenticated = Authorized(c), admin = IsAdmin(c), maxFileSize = maxBytes,
    addresses = LocalAddresses(settings.Port)
}));
app.MapPost("/api/login", async (HttpContext c) =>
{
    var ip = c.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    var now = DateTimeOffset.UtcNow;
    if (loginAttempts.TryGetValue(ip, out var previous) && previous.Count >= 10 && previous.Since.AddMinutes(5) > now)
        return Results.Json(new { error = "尝试次数过多，请 5 分钟后重试。" }, statusCode: 429);
    var limit = c.Features.Get<IHttpMaxRequestBodySizeFeature>();
    if (limit is { IsReadOnly: false }) limit.MaxRequestBodySize = 4096;
    Login? body;
    try { body = await c.Request.ReadFromJsonAsync<Login>(c.RequestAborted); }
    catch (JsonException) { return Results.BadRequest(new { error = "请求格式无效。" }); }
    var password = body?.Password ?? "";
    var admin = PasswordMatches(password, settings.AdminPassword);
    if (!admin && (string.IsNullOrEmpty(settings.AccessPassword) || !PasswordMatches(password, settings.AccessPassword)))
    {
        loginAttempts.AddOrUpdate(ip, new Attempt(now, 1), (_, old) => old.Since.AddMinutes(5) < now ? new(now, 1) : old with { Count = old.Count + 1 });
        return Results.Json(new { error = "密码错误，请重试。" }, statusCode: 401);
    }
    loginAttempts.TryRemove(ip, out _);
    foreach (var expired in sessions.Where(x => x.Value.Expires < now)) sessions.TryRemove(expired.Key, out _);
    var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    sessions[token] = new Session(admin, now.AddHours(12));
    c.Response.Cookies.Append("lanshare-session", token, new CookieOptions { HttpOnly = true, SameSite = SameSiteMode.Strict, MaxAge = TimeSpan.FromHours(12), Path = "/", IsEssential = true });
    return Results.Ok(new { admin });
});
app.MapPost("/api/logout", (HttpContext c) =>
{
    if (c.Request.Cookies.TryGetValue("lanshare-session", out var token)) sessions.TryRemove(token, out _);
    c.Response.Cookies.Delete("lanshare-session"); return Results.Ok();
});
app.MapGet("/api/files", () =>
{
    var entries = Directory.EnumerateDirectories(filesDir).Select(d => Find(Path.GetFileName(d))).OfType<FileEntry>().OrderByDescending(x => x.UploadedAt).ToArray();
    return Results.Ok(entries);
});
app.MapPut("/api/files", async (HttpContext c) =>
{
    var rawName = c.Request.Query["name"].ToString();
    var name = rawName.Replace('\\', '/').Split('/').Last().Trim();
    if (string.IsNullOrWhiteSpace(name) || name.Length > 240 || name.Any(char.IsControl))
        return Results.BadRequest(new { error = "文件名不能为空、包含控制字符或超过 240 个字符。" });
    if (c.Request.ContentLength > maxBytes) return Results.Json(new { error = "文件超过大小限制。" }, statusCode: 413);
    if (!await uploadSlots.WaitAsync(0, c.RequestAborted))
        return Results.Json(new { error = "当前上传任务较多，请稍后重试。" }, statusCode: 429);
    var id = Guid.NewGuid().ToString("N");
    var temp = Path.Combine(stagingDir, id);
    try
    {
        Directory.CreateDirectory(temp);
        long total = 0;
        await using (var output = new FileStream(Path.Combine(temp, "content.bin"), FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            var buffer = new byte[128 * 1024];
            int count;
            while ((count = await c.Request.Body.ReadAsync(buffer, c.RequestAborted)) > 0)
            {
                total += count;
                if (total > maxBytes) return Results.Json(new { error = "文件超过大小限制。" }, statusCode: 413);
                await output.WriteAsync(buffer.AsMemory(0, count), c.RequestAborted);
            }
            await output.FlushAsync(c.RequestAborted);
        }
        var entry = new FileEntry(id, name, total, DateTimeOffset.UtcNow);
        await File.WriteAllTextAsync(Path.Combine(temp, "metadata.json"), JsonSerializer.Serialize(entry, json), c.RequestAborted);
        Directory.Move(temp, Path.Combine(filesDir, id));
        return Results.Created($"/api/files/{id}/download", entry);
    }
    finally
    {
        try { if (Directory.Exists(temp)) Directory.Delete(temp, true); } catch (IOException) { }
        uploadSlots.Release();
    }
});
app.MapMethods("/api/files/{id}/download", ["GET", "HEAD"], (string id) =>
{
    var entry = Find(id);
    return entry is null ? Results.NotFound(new { error = "文件不存在或已被删除。" }) :
        Results.File(Path.Combine(filesDir, id, "content.bin"), "application/octet-stream", entry.Name, enableRangeProcessing: true);
});
app.MapDelete("/api/files/{id}", (string id, HttpContext c) =>
{
    if (!IsAdmin(c)) return Results.Json(new { error = "删除文件需要管理员登录。" }, statusCode: 403);
    if (Find(id) is null) return Results.NotFound(new { error = "文件不存在或已被删除。" });
    Directory.Delete(Path.Combine(filesDir, id), true);
    return Results.NoContent();
});

try
{
    await app.StartAsync();
    Console.WriteLine("\n  局域网文件站 LanShare 1.0\n");
    Console.WriteLine($"  本机访问：http://localhost:{settings.Port}");
    foreach (var address in LocalAddresses(settings.Port)) Console.WriteLine($"  局域网访问：{address}");
    Console.WriteLine($"  存储目录：{dataDir}\n  配置文件：{settingsPath}\n  单文件上限：{settings.MaxFileSizeMB} MB");
    Console.WriteLine($"  访问密码：{(string.IsNullOrEmpty(settings.AccessPassword) ? "未设置（局域网用户可上传、下载）" : "已启用")}");
    Console.WriteLine($"  管理员密码：{settings.AdminPassword}\n\n  按 Ctrl+C 停止服务。关闭窗口也会停止服务。\n");
    if (settings.OpenBrowser && !args.Contains("--no-browser") && OperatingSystem.IsWindows())
    { try { Process.Start(new ProcessStartInfo($"http://localhost:{settings.Port}") { UseShellExecute = true }); } catch { } }
    await app.WaitForShutdownAsync();
    return 0;
}
catch (IOException ex)
{
    Console.Error.WriteLine($"启动失败：端口 {settings.Port} 可能已被占用。请检查是否重复启动，或修改 lanshare.json 的 port。\n{ex.Message}");
    return 1;
}

static byte[] ReadResource(string name)
{
    using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"LanShare.Web.{name}") ?? throw new InvalidOperationException($"缺少网页资源：{name}");
    using var buffer = new MemoryStream(); stream.CopyTo(buffer); return buffer.ToArray();
}
static string[] LocalAddresses(int port)
{
    try
    {
        return NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel))
            .OrderByDescending(n => n.GetIPProperties().GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork))
            .SelectMany(n => n.GetIPProperties().UnicastAddresses)
            .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
            .Select(a => a.Address.ToString())
            .Where(a => !a.StartsWith("127.") && !a.StartsWith("169.254.") && !a.StartsWith("198.18.") && !a.StartsWith("198.19."))
            .Distinct().Select(ip => $"http://{ip}:{port}").ToArray();
    }
    catch { return []; }
}
record FileEntry(string Id, string Name, long Size, DateTimeOffset UploadedAt);
record Session(bool Admin, DateTimeOffset Expires);
record Attempt(DateTimeOffset Since, int Count);
record Login(string Password);
class Settings
{
    public int Port { get; set; } = 8080;
    public string DataDirectory { get; set; } = "data";
    public long MaxFileSizeMB { get; set; } = 2048;
    public bool OpenBrowser { get; set; } = true;
    public string AccessPassword { get; set; } = "";
    public string AdminPassword { get; set; } = "";
}
