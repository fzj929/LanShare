using System.Diagnostics;
using LanShare.Api;
using LanShare.Configuration;
using LanShare.Infrastructure;
using LanShare.Middleware;
using LanShare.Services;

AppSettings settings;
FileStore files;
try
{
    settings = AppSettings.Load(AppContext.BaseDirectory);
    files = new FileStore(settings);
}
catch (Exception ex)
{
    Console.Error.WriteLine($"初始化失败：{ex.Message}\n请检查程序目录的写入权限和 lanshare.json 配置。");
    return 1;
}

var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { Args = args, ContentRootPath = AppContext.BaseDirectory });
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(o => o.SingleLine = true);
builder.Logging.SetMinimumLevel(LogLevel.Warning);
builder.Services.AddSingleton(settings);
builder.Services.AddSingleton(files);
builder.Services.AddSingleton<AuthService>();
builder.WebHost.ConfigureKestrel(o =>
{
    o.ListenAnyIP(settings.Port);
    o.Limits.MaxRequestBodySize = settings.MaxFileSizeBytes;
    o.AddServerHeader = false;
});

var app = builder.Build();
app.UseMiddleware<RequestSecurityMiddleware>();
app.MapPageEndpoints();
app.MapStatusEndpoints();
app.MapAuthEndpoints();
app.MapFileEndpoints();

try
{
    await app.StartAsync();
    Console.WriteLine("\n  局域网文件站 LanShare\n");
    Console.WriteLine($"  本机访问：http://localhost:{settings.Port}");
    foreach (var address in NetworkAddresses.GetLocal(settings.Port)) Console.WriteLine($"  局域网访问：{address}");
    Console.WriteLine($"  存储目录：{settings.DataDirectory}\n  配置文件：{settings.ConfigPath}\n  单文件上限：{settings.MaxFileSizeMB} MB");
    Console.WriteLine($"  访问密码：{(string.IsNullOrEmpty(settings.AccessPassword) ? "未设置（局域网用户可上传、下载）" : "已启用")}");
    Console.WriteLine($"  管理员密码：{settings.AdminPassword}\n\n  按 Ctrl+C 停止服务。关闭窗口也会停止服务。\n");
    if (settings.OpenBrowser && !args.Contains("--no-browser") && OperatingSystem.IsWindows())
    {
        try { Process.Start(new ProcessStartInfo($"http://localhost:{settings.Port}") { UseShellExecute = true }); }
        catch { /* Opening a browser is optional. */ }
    }
    await app.WaitForShutdownAsync();
    return 0;
}
catch (IOException ex)
{
    Console.Error.WriteLine($"启动失败：端口 {settings.Port} 可能已被占用。请检查是否重复启动，或修改 lanshare.json 的 port。\n{ex.Message}");
    return 1;
}
