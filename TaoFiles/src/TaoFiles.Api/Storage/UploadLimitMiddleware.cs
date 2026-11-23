using Microsoft.AspNetCore.Http.Features;

namespace TaoFiles.Api.Storage;

/// <summary>
/// 上传体积中间件（⑬）：按上传上限提供口动态收紧请求体上限（上限 + 表单开销余量），
/// 不再吃框架默认值；超限时 Kestrel 直接断开，业务层另有明确的中文报错兜底。
/// </summary>
public class UploadLimitMiddleware(RequestDelegate next, IUploadLimitProvider limitProvider)
{
    /// <summary>multipart 表单元数据开销余量：请求体上限在上传上限之上再放宽 64KB。</summary>
    public const long FormOverheadBytes = 64 * 1024;

    public async Task InvokeAsync(HttpContext context)
    {
        var limit = await limitProvider.GetLimitBytesAsync(context.RequestAborted);
        if (context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { } feature)
            feature.MaxRequestBodySize = limit + FormOverheadBytes;
        await next(context);
    }
}
