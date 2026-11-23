using TaoAdmin.Application.Services;
using TaoAdmin.Common.Auth;
using TaoAdmin.Common.Http;
using TaoAdmin.Common.Web;
using TaoAdmin.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace TaoAdmin.IdentityService.Controllers;

/// <summary>
/// 代码生成器入口（⑲）：表设计维护、同步建库、一键生成全套增删改查前后端代码、生成记录查看。
/// </summary>
[Route("codegen")]
public class CodeGenController : BaseApiController
{
    private readonly CodeGenAppService _svc;
    public CodeGenController(CodeGenAppService svc) => _svc = svc;

    /// <summary>表设计分页</summary>
    [HttpGet("designs")]
    [Permission("sys:codegen:manage")]
    public async Task<ApiResult<PageResult<SysCodeGenTable>>> Designs([FromQuery] int pageIndex = 1, [FromQuery] int pageSize = 10, [FromQuery] string? keyword = null)
        => ApiOk(await _svc.PageAsync(pageIndex, pageSize, keyword));

    /// <summary>保存表设计（新建或更新）</summary>
    [HttpPost("designs")]
    [Permission("sys:codegen:manage")]
    public async Task<ApiResult<object>> SaveDesign([FromBody] CodegenDesignSaveRequest req)
    {
        var id = await _svc.SaveDesignAsync(req);
        return ApiOk<object>(id.ToString());
    }

    /// <summary>表设计详情</summary>
    [HttpGet("designs/{id}")]
    [Permission("sys:codegen:manage")]
    public async Task<ApiResult<SysCodeGenTable>> GetDesign(string id)
        => ApiOk(await _svc.GetAsync(long.Parse(id)));

    /// <summary>删除表设计（软删，不动已生成的库表与代码）</summary>
    [HttpDelete("designs/{id}")]
    [Permission("sys:codegen:manage")]
    public async Task<ApiResult<object>> DeleteDesign(string id)
    {
        await _svc.DeleteAsync(long.Parse(id));
        return ApiOk("删除成功");
    }

    /// <summary>同步到数据库：建表/加列自动执行；删列等破坏性变更仅返回语句待确认</summary>
    [HttpPost("designs/{id}/sync")]
    [Permission("sys:codegen:manage")]
    public async Task<ApiResult<CodegenSyncResult>> Sync(string id)
        => ApiOk(await _svc.SyncToDatabaseAsync(long.Parse(id)));

    /// <summary>执行经确认的破坏性变更语句（仅允许单条 ALTER TABLE）</summary>
    [HttpPost("destructive")]
    [Permission("sys:codegen:manage")]
    public async Task<ApiResult<object>> ExecuteDestructive([FromBody] Dictionary<string, string> body)
    {
        body.TryGetValue("sql", out var sql);
        await _svc.ExecuteDestructiveAsync(sql ?? string.Empty);
        return ApiOk("已执行");
    }

    /// <summary>一键生成全套增删改查前后端代码（落生成记录）</summary>
    [HttpPost("designs/{id}/generate")]
    [Permission("sys:codegen:manage")]
    public async Task<ApiResult<CodegenGenerateResult>> Generate(string id, [FromQuery] string? mode = null)
        => ApiOk(await _svc.GenerateAsync(long.Parse(id), mode ?? "generate"));

    /// <summary>生成记录分页</summary>
    [HttpGet("records")]
    [Permission("sys:codegen:manage")]
    public async Task<ApiResult<PageResult<SysCodeGenRecord>>> Records([FromQuery] int pageIndex = 1, [FromQuery] int pageSize = 10, [FromQuery] string? keyword = null)
        => ApiOk(await _svc.PageRecordsAsync(pageIndex, pageSize, keyword));

    /// <summary>生成记录详情（含全部生成文件内容）</summary>
    [HttpGet("records/{id}")]
    [Permission("sys:codegen:manage")]
    public async Task<ApiResult<object>> Record(string id)
    {
        var (record, files) = await _svc.GetRecordAsync(long.Parse(id));
        object data = new { record.Id, record.TableName, record.Mode, record.FileCount, record.ElapsedMs, record.CreatedAt, files };
        return ApiOk(data);
    }
}
