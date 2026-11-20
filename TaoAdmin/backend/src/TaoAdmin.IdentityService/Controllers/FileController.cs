using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaoAdmin.Application.Services;
using TaoAdmin.Common.Auth;
using TaoAdmin.Common.Http;
using TaoAdmin.Common.Web;

namespace TaoAdmin.IdentityService.Controllers;

/// <summary>
/// 文件中心入口：上传（MD5 秒传去重）、分页查询、下载（带扩展名校验）、删除。
/// 上传/管理要求 sys:file:manage 权限；下载登录即可（业务侧按 FileUrl 交付）。
/// </summary>
[Route("files")]
public class FileController : BaseApiController
{
    private readonly FileAppService _svc;
    public FileController(FileAppService svc) => _svc = svc;

    /// <summary>分页查询文件记录（keyword 按原文件名模糊）</summary>
    [HttpGet]
    [Permission("sys:file:manage")]
    public async Task<ApiResult<PageResult<TaoAdmin.Domain.Entities.SysFile>>> Page(
        [FromQuery] int pageIndex = 1, [FromQuery] int pageSize = 10, [FromQuery] string? keyword = null)
        => ApiOk(await _svc.PageAsync(pageIndex, pageSize, keyword));

    /// <summary>上传文件（multipart/form-data，字段名 file）</summary>
    [HttpPost("upload")]
    [Permission("sys:file:manage")]
    [RequestSizeLimit(FileAppService.MaxUploadSize + 8192)]
    public async Task<ApiResult<FileUploadResult>> Upload(IFormFile file)
    {
        if (file is null || file.Length == 0) return ApiFail<FileUploadResult>("请选择要上传的文件");
        await using var stream = file.OpenReadStream();
        var result = await _svc.UploadAsync(stream, file.FileName, file.ContentType, User.GetUserId(), User.GetUserName());
        return ApiOk(result);
    }

    /// <summary>下载文件：按相对路径取物理流，文件名用登记的原文件名</summary>
    [HttpGet("download")]
    [Authorize]
    public async Task<IActionResult> Download([FromQuery] string url)
    {
        if (string.IsNullOrWhiteSpace(url) || !url.StartsWith("/uploads", StringComparison.Ordinal))
            return NotFound();
        var opened = await _svc.OpenAsync(url);
        if (opened is null) return NotFound();
        var (row, content) = opened.Value;
        return File(content, row.ContentType ?? "application/octet-stream", row.FileName);
    }

    /// <summary>删除文件（软删 + 物理删）</summary>
    [HttpDelete("{id}")]
    [Permission("sys:file:manage")]
    public async Task<ApiResult<object>> Delete(string id)
    {
        await _svc.DeleteAsync(long.Parse(id));
        return ApiOk("删除成功");
    }
}
