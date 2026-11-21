using TaoAdmin.Application.Services;
using TaoAdmin.Common.Auth;
using TaoAdmin.Common.Http;
using TaoAdmin.Common.Web;
using TaoAdmin.Domain.Entities;
using Microsoft.AspNetCore.Mvc;

namespace TaoAdmin.IdentityService.Controllers;

/// <summary>数据字典入口：字典类型维护、明细维护、前端下拉数据源。</summary>
[Route("dict")]
public class DictController : BaseApiController
{
    private readonly DictAppService _svc;
    public DictController(DictAppService svc) => _svc = svc;

    /// <summary>字典类型分页</summary>
    [HttpGet("types")]
    [Permission("sys:dict:manage")]
    public async Task<ApiResult<PageResult<DictTypeDto>>> Types([FromQuery] int pageIndex = 1, [FromQuery] int pageSize = 10,
        [FromQuery] string? keyword = null)
        => ApiOk(await _svc.PageTypesAsync(Math.Max(pageIndex, 1), Math.Min(Math.Max(pageSize, 1), 100), keyword));

    /// <summary>按编码取启用的字典明细（前端下拉框数据源，无需管理权限）</summary>
    [HttpGet("items/{dictCode}")]
    public async Task<ApiResult<List<DictDataDto>>> Items(string dictCode) => ApiOk(await _svc.GetItemsAsync(dictCode));

    /// <summary>新增/编辑字典类型（带 id 为编辑）</summary>
    [HttpPost("types")]
    [HttpPut("types/{id?}")]
    [Permission("sys:dict:manage")]
    public async Task<ApiResult<object>> SaveType([FromBody] DictTypeSaveRequest req, string? id = null)
    {
        await _svc.SaveTypeAsync(req, id);
        return ApiOk(string.IsNullOrEmpty(id) ? "新增成功" : "修改成功");
    }

    /// <summary>新增/编辑字典数据（带 id 为编辑）</summary>
    [HttpPost("data")]
    [HttpPut("data/{id?}")]
    [Permission("sys:dict:manage")]
    public async Task<ApiResult<object>> SaveData([FromBody] DictDataSaveRequest req, string? id = null)
    {
        await _svc.SaveDataAsync(req, id);
        return ApiOk(string.IsNullOrEmpty(id) ? "新增成功" : "修改成功");
    }

    /// <summary>删除字典类型（连同明细）</summary>
    [HttpDelete("types/{id}")]
    [Permission("sys:dict:manage")]
    public async Task<ApiResult<object>> DeleteType(string id)
    {
        await _svc.DeleteTypeAsync(id);
        return ApiOk("删除成功");
    }

    /// <summary>删除字典数据</summary>
    [HttpDelete("data/{id}")]
    [Permission("sys:dict:manage")]
    public async Task<ApiResult<object>> DeleteData(string id)
    {
        await _svc.DeleteDataAsync(id);
        return ApiOk("删除成功");
    }
}

/// <summary>操作日志入口：审计记录分页查询（登录成功/失败也记录在内）。</summary>
[Route("operation-logs")]
public class OperationLogController : BaseApiController
{
    private readonly OperationLogAppService _svc;
    public OperationLogController(OperationLogAppService svc) => _svc = svc;

    /// <summary>操作日志分页（类别：login 登录 / operation 操作；cursor=上一页 NextCursor 时走游标深翻页）</summary>
    [HttpGet]
    [Permission("sys:log:query")]
    public async Task<ApiResult<PageResult<SysOperationLog>>> Page([FromQuery] int pageIndex = 1, [FromQuery] int pageSize = 10,
        [FromQuery] string? category = null, [FromQuery] string? userName = null,
        [FromQuery] int? success = null, [FromQuery] DateTime? begin = null, [FromQuery] DateTime? end = null,
        [FromQuery] string? cursor = null)
        => ApiOk(await _svc.PageAsync(pageIndex, pageSize, category, userName, success, begin, end, cursor));
}
