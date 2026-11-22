using TaoAdmin.Application.Dtos;
using TaoAdmin.Application.Services;
using TaoAdmin.Common.Auth;
using TaoAdmin.Common.Http;
using TaoAdmin.Common.Idempotency;
using TaoAdmin.Common.MultiTenancy;
using TaoAdmin.Common.Web;
using TaoAdmin.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace TaoAdmin.IdentityService.Controllers;

/// <summary>多租户管理入口（平台管理员）。业务在 TenantAppService。</summary>
[Route("tenants")]
public class TenantController : BaseApiController
{
    private readonly TenantAppService _svc;
    public TenantController(TenantAppService svc) => _svc = svc;

    /// <summary>租户列表</summary>
    [HttpGet]
    [Permission("sys:tenant:manage")]
    public async Task<ApiResult<List<SysTenant>>> List() => ApiOk(await _svc.ListAsync());

    /// <summary>新增/修改租户</summary>
    [HttpPost]
    [HttpPut("{id?}")]
    [Permission("sys:tenant:manage")]
    public async Task<ApiResult<object>> Save([FromBody] TenantSaveRequest req)
    {
        await _svc.SaveAsync(string.IsNullOrEmpty(req.id) ? 0 : long.Parse(req.id),
            req.tenantName, req.tenantCode, req.contact, req.remark, req.status);
        return ApiOk(string.IsNullOrEmpty(req.id) ? "新增成功" : "修改成功");
    }

    /// <summary>删除租户（软删）</summary>
    [HttpDelete("{id}")]
    [Permission("sys:tenant:manage")]
    public async Task<ApiResult<object>> Delete(string id)
    {
        await _svc.DeleteAsync(long.Parse(id));
        return ApiOk("删除成功");
    }

    /// <summary>批量删除租户（可容错逐条执行，返回实际删除数与失败明细）</summary>
    [HttpPost("batch-delete")]
    [Permission("sys:tenant:manage")]
    public async Task<ApiResult<object>> BatchDelete([FromBody] BatchIdsRequest req)
    {
        var (deleted, failed) = await _svc.BatchDeleteAsync(req.Ids);
        return ApiOk<object>(new { deleted, failed }, "批量删除完成");
    }
}

/// <summary>站内消息入口：列表、已读、消息队列演示。</summary>
[Route("messages")]
public class MessageController : BaseApiController
{
    private readonly MessageAppService _svc;
    public MessageController(MessageAppService svc) => _svc = svc;

    /// <summary>消息分页</summary>
    [HttpGet]
    public async Task<ApiResult<PageResult<SysMessage>>> Page(
        [FromQuery] int pageIndex = 1, [FromQuery] int pageSize = 10)
    {
        var (total, items) = await _svc.PageAsync(pageIndex, pageSize, User.GetUserId(), User.IsSuperAdmin());
        return ApiOk(new PageResult<SysMessage>(total, pageIndex, pageSize, items));
    }

    /// <summary>标记已读</summary>
    [HttpPut("{id}/read")]
    public async Task<ApiResult<object>> MarkRead(string id)
    {
        await _svc.MarkReadAsync(long.Parse(id));
        return ApiOk("已标记已读");
    }

    /// <summary>批量标记已读（返回实际更新数）</summary>
    [HttpPost("batch-read")]
    public async Task<ApiResult<object>> BatchRead([FromBody] BatchIdsRequest req)
    {
        var updated = await _svc.BatchMarkReadAsync(req.Ids);
        return ApiOk<object>(new { updated }, "已批量标记已读");
    }

    /// <summary>批量删除消息（软删，返回实际删除数）</summary>
    [HttpPost("batch-delete")]
    public async Task<ApiResult<object>> BatchDelete([FromBody] BatchIdsRequest req)
    {
        var deleted = await _svc.BatchDeleteAsync(req.Ids);
        return ApiOk<object>(new { deleted }, "批量删除完成");
    }

    /// <summary>【消息队列演示】向指定主题发布一条测试消息，消费者收到后落库为站内消息</summary>
    [HttpPost("demo/publish")]
    [Idempotent]
    public async Task<ApiResult<object>> PublishDemo([FromBody] Dictionary<string, string> body)
    {
        body.TryGetValue("topic", out var topic);
        body.TryGetValue("content", out var content);
        await _svc.PublishDemoAsync(topic ?? "", content ?? "");
        return ApiOk("消息已发布，消费者处理完成后可在消息列表查看");
    }

    /// <summary>【消息队列演示】队列状态（提供方 + 各主题积压条数）</summary>
    [HttpGet("demo/stats")]
    public ApiResult<object> Stats() => ApiOk<object>(_svc.GetStats());
}

/// <summary>工作流入口：流程定义、发起审批、待办与审批操作。业务在 WorkflowAppService。</summary>
[Route("workflow")]
public class WorkflowController : BaseApiController
{
    private readonly WorkflowAppService _svc;
    public WorkflowController(WorkflowAppService svc) => _svc = svc;

    /// <summary>流程定义列表（平台模板 + 本租户）</summary>
    [HttpGet("definitions")]
    public async Task<ApiResult<List<WorkflowDefDto>>> Definitions() => ApiOk(await _svc.GetDefinitionsAsync());

    /// <summary>发起审批（重复提交用 Idempotency-Key 请求头防重）</summary>
    [HttpPost("instances")]
    [Idempotent]
    public async Task<ApiResult<object>> Submit([FromBody] WorkflowSubmitRequest req)
    {
        var user = GetCurrentUser();
        var instanceId = await _svc.SubmitAsync(req, user.userId, user.nickname ?? user.username, User.GetTenantId());
        return ApiOk<object>(new { instanceId }, "审批已提交");
    }

    /// <summary>我的/全部流程实例分页（超管看全部）</summary>
    [HttpGet("instances")]
    public async Task<ApiResult<PageResult<WorkflowInstanceDto>>> Instances(
        [FromQuery] int pageIndex = 1, [FromQuery] int pageSize = 10, [FromQuery] int? status = null)
    {
        var page = await _svc.GetInstancesAsync(pageIndex, pageSize, User.GetUserId(), User.IsSuperAdmin(), status);
        return ApiOk(page);
    }

    /// <summary>流程实例详情（含任务历史）</summary>
    [HttpGet("instances/{id}")]
    public async Task<ApiResult<object>> Detail(string id)
    {
        var (instance, tasks) = await _svc.GetDetailAsync(long.Parse(id));
        return ApiOk<object>(new { instance, tasks });
    }

    /// <summary>我的待办任务（拥有对应审批角色的任务；超管看全部）</summary>
    [HttpGet("tasks")]
    public async Task<ApiResult<List<WorkflowTaskDto>>> MyTasks()
    {
        var (roles, isSuper) = (User.GetRoles(), User.IsSuperAdmin());
        return ApiOk(await _svc.GetMyTasksAsync(User.GetUserId(), roles, isSuper, User.GetTenantId()));
    }

    /// <summary>审批：通过/驳回（需拥有任务指定角色）</summary>
    [HttpPost("tasks/{id}/approve")]
    public async Task<ApiResult<object>> Approve(string id, [FromBody] WorkflowApproveRequest req)
    {
        var user = GetCurrentUser();
        await _svc.ApproveAsync(long.Parse(id), req, User.GetUserId(), user.nickname ?? user.username,
            User.GetRoles(), User.IsSuperAdmin(), User.GetTenantId());
        return ApiOk(req.approved ? "审批通过" : "已驳回");
    }

    private (long userId, string? nickname, string username) GetCurrentUser()
    {
        var userId = User.GetUserId();
        var principal = User;
        var nickname = principal.FindFirst("NickName")?.Value;
        var username = principal.Identity?.Name ?? "";
        return (userId, nickname, username);
    }
}

/// <summary>租户保存请求</summary>
public class TenantSaveRequest
{
    public string? id { get; set; }
    public string tenantName { get; set; } = "";
    public string tenantCode { get; set; } = "";
    public string? contact { get; set; }
    public string? remark { get; set; }
    public int status { get; set; } = 1;
}
