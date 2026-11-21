using TaoFiles.Api.Controllers;
using TaoFiles.Api.Storage;
using Microsoft.AspNetCore.Mvc;

namespace TaoFiles.Api.Controllers;

/// <summary>文件服务对外接口：业务系统集成入口</summary>
[ApiController]
public class FileController(FileAppService svc, IFileStorageProvider storage) : ControllerBase
{
    /// <summary>上传文件（multipart/form-data，字段名 file）</summary>
    /// <remarks>
    /// 集成示例：业务系统上传后只需保存返回的 fileUrl（相对路径）。
    /// 相同内容的文件重复上传会命中复用（reused=true），不占额外空间。
    /// </remarks>
    [HttpPost("/api/files/upload")]
    [RequestSizeLimit(200 * 1024 * 1024)]
    public async Task<IActionResult> Upload([FromForm] IFormFile file,
        [FromForm] string? uploadBy, [FromForm] string? source)
    {
        var result = await svc.UploadAsync(file, uploadBy, source);
        return Ok(new { code = 200, msg = "ok", data = result });
    }

    /// <summary>文件分页列表（cursor=上一页 nextCursor 时走游标深翻页）</summary>
    [HttpGet("/api/files")]
    public async Task<IActionResult> Page([FromQuery] int pageIndex = 1, [FromQuery] int pageSize = 10,
        [FromQuery] string? keyword = null, [FromQuery] string? source = null, [FromQuery] string? cursor = null)
    {
        var result = await svc.PageAsync(Math.Max(pageIndex, 1), Math.Min(Math.Max(pageSize, 1), 100), keyword, source, cursor);
        return Ok(new { code = 200, msg = "ok", data = result });
    }

    /// <summary>删除文件（physicalDelete=true 连磁盘文件一起清）</summary>
    [HttpDelete("/api/files/{id}")]
    public async Task<IActionResult> Delete(string id, [FromQuery] bool physicalDelete = false)
    {
        await svc.DeleteAsync(id, physicalDelete);
        return Ok(new { code = 200, msg = "ok" });
    }

    /// <summary>下载/读取文件内容</summary>
    [HttpGet("/f/{**path}")]
    public async Task<IActionResult> Read(string path)
    {
        var stream = await storage.OpenAsync($"/uploads/{path}");
        if (stream is null) return NotFound(new { code = 404, msg = "文件不存在" });
        return File(stream, "application/octet-stream", Path.GetFileName(path));
    }
}
