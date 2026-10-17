using LanShare.Configuration;
using LanShare.Models;
using LanShare.Services;

namespace LanShare.Api;

public static class FileEndpoints
{
    public static void MapFileEndpoints(this WebApplication app)
    {
        app.MapGet("/api/files", (FileStore files) => Results.Ok(files.List()));
        app.MapPut("/api/files", UploadAsync);
        app.MapMethods("/api/files/{id}/download", ["GET", "HEAD"], Download);
        app.MapDelete("/api/files/{id}", Delete);
    }

    private static async Task<IResult> UploadAsync(HttpContext context, FileStore files, AppSettings settings)
    {
        var name = context.Request.Query["name"].ToString().Replace('\\', '/').Split('/').Last().Trim();
        if (string.IsNullOrWhiteSpace(name) || name.Length > 240 || name.Any(char.IsControl))
            return Results.BadRequest(new { error = "文件名不能为空、包含控制字符或超过 240 个字符。" });
        if (context.Request.ContentLength > settings.MaxFileSizeBytes) return TooLarge();
        if (!await files.UploadSlots.WaitAsync(0, context.RequestAborted))
            return Results.Json(new { error = "当前上传任务较多，请稍后重试。" }, statusCode: 429);
        var id = Guid.NewGuid().ToString("N");
        var temp = Path.Combine(files.StagingDirectory, id);
        try
        {
            Directory.CreateDirectory(temp);
            long total = 0;
            await using (var output = new FileStream(Path.Combine(temp, "content.bin"), FileMode.CreateNew,
                FileAccess.Write, FileShare.None, 128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                var buffer = new byte[128 * 1024];
                int count;
                while ((count = await context.Request.Body.ReadAsync(buffer, context.RequestAborted)) > 0)
                {
                    total += count;
                    if (total > settings.MaxFileSizeBytes) return TooLarge();
                    await output.WriteAsync(buffer.AsMemory(0, count), context.RequestAborted);
                }
                await output.FlushAsync(context.RequestAborted);
            }
            var entry = new FileEntry(id, name, total, DateTimeOffset.UtcNow);
            await files.CommitAsync(temp, entry, context.RequestAborted);
            return Results.Created($"/api/files/{id}/download", entry);
        }
        finally
        {
            try { if (Directory.Exists(temp)) Directory.Delete(temp, true); }
            catch (IOException) { }
            files.UploadSlots.Release();
        }
    }

    private static IResult Download(string id, FileStore files)
    {
        var entry = files.Find(id);
        return entry is null ? Results.NotFound(new { error = "文件不存在或已被删除。" }) :
            Results.File(files.ContentPath(id), "application/octet-stream", entry.Name, enableRangeProcessing: true);
    }

    private static IResult Delete(string id, HttpContext context, FileStore files, AuthService auth)
    {
        if (!auth.IsAdmin(context)) return Results.Json(new { error = "删除文件需要管理员登录。" }, statusCode: 403);
        if (files.Find(id) is null) return Results.NotFound(new { error = "文件不存在或已被删除。" });
        files.Delete(id);
        return Results.NoContent();
    }

    private static IResult TooLarge() => Results.Json(new { error = "文件超过大小限制。" }, statusCode: 413);
}
