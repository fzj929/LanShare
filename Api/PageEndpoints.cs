using System.Reflection;

namespace LanShare.Api;

public static class PageEndpoints
{
    public static void MapPageEndpoints(this WebApplication app)
    {
        const string prefix = "LanShare.Web.";
        var assembly = Assembly.GetExecutingAssembly();
        foreach (var resource in assembly.GetManifestResourceNames().Where(n => n.StartsWith(prefix, StringComparison.Ordinal)))
        {
            var name = resource[prefix.Length..];
            var extension = Path.GetExtension(name);
            var type = extension switch
            {
                ".html" => "text/html; charset=utf-8",
                ".css" => "text/css; charset=utf-8",
                ".js" => "text/javascript; charset=utf-8",
                _ => null
            };
            if (type is null) continue;
            var route = name == "index.html" ? "/" : "/" + name[..^extension.Length].Replace('.', '/') + extension;
            using var stream = assembly.GetManifestResourceStream(resource)!;
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            var bytes = buffer.ToArray();
            app.MapGet(route, () => Results.Bytes(bytes, type));
        }
    }
}
