using System.Text.Json;
using TaoAdmin.Common.Http;
using TaoAdmin.Domain;
using TaoAdmin.Domain.Entities;
using SqlSugar;

namespace TaoAdmin.Application.Services;

// ==================== DTO ====================

public class WorkflowNodeDto
{
    /// <summary>节点名称（如：组长审批）</summary>
    public string name { get; set; } = string.Empty;
    /// <summary>审批角色编码（该角色的人可处理本节点）</summary>
    public string approverRole { get; set; } = string.Empty;
}

public class WorkflowDefDto
{
    public string id { get; set; } = "";
    public string workflowName { get; set; } = "";
    public string workflowCode { get; set; } = "";
    public string? description { get; set; }
    public List<WorkflowNodeDto> nodes { get; set; } = new();
    public int status { get; set; }
}

public class WorkflowInstanceDto
{
    public string id { get; set; } = "";
    public string workflowName { get; set; } = "";
    public string bizTitle { get; set; } = "";
    public string? remark { get; set; }
    public string applicantName { get; set; } = "";
    public string currentNode { get; set; } = "";
    /// <summary>1 进行中 2 已通过 3 已驳回 4 已取消</summary>
    public int status { get; set; }
    public string statusText { get; set; } = "";
    public DateTime? finishedAt { get; set; }
    public DateTime createdAt { get; set; }
}

public class WorkflowTaskDto
{
    public string id { get; set; } = "";
    public string instanceId { get; set; } = "";
    public string bizTitle { get; set; } = "";
    public string workflowName { get; set; } = "";
    public string nodeName { get; set; } = "";
    public string applicantName { get; set; } = "";
    /// <summary>0 待审批 1 通过 2 驳回</summary>
    public int status { get; set; }
    public string? comment { get; set; }
    public string? handlerName { get; set; }
    public DateTime? handleTime { get; set; }
    public DateTime createdAt { get; set; }
}

public class WorkflowSubmitRequest
{
    /// <summary>流程编码（如 leave_audit）</summary>
    public string workflowCode { get; set; } = "";
    /// <summary>业务标题（如：张三的请假申请）</summary>
    public string bizTitle { get; set; } = "";
    public string? remark { get; set; }
}

public class WorkflowApproveRequest
{
    /// <summary>true 通过 / false 驳回</summary>
    public bool approved { get; set; }
    public string? comment { get; set; }
}

// ==================== 服务 ====================

/// <summary>
/// 工作流应用服务：线性审批流引擎。
/// 节点按 NodesJson 顺序执行，每级审批人生成待办任务，拥有对应角色的人可审批；
/// 最后一级通过后流程结束，任一节点驳回则整个流程终止。
/// </summary>
public class WorkflowAppService
{
    private readonly ISqlSugarClient _db;
    private readonly Common.Snowflake.SnowflakeIdGenerator _snowflake;

    public WorkflowAppService(ISqlSugarClient db, Common.Snowflake.SnowflakeIdGenerator snowflake)
    {
        _db = db; _snowflake = snowflake;
    }

    private long NewId() => _snowflake.NextId();
    private static string StatusText(int s) => s switch { 1 => "进行中", 2 => "已通过", 3 => "已驳回", 4 => "已取消", _ => "未知" };

    /// <summary>流程定义列表（按当前租户可见：平台模板 + 本租户私有）</summary>
    public async Task<List<WorkflowDefDto>> GetDefinitionsAsync()
    {
        var list = await _db.Queryable<SysWorkflow>()
            .Where(w => w.IsDeleted == false && w.Status == 1)
            .OrderBy(w => w.Id)
            .ToListAsync();
        return list.Select(w => new WorkflowDefDto
        {
            id = w.Id.ToString(),
            workflowName = w.WorkflowName,
            workflowCode = w.WorkflowCode,
            description = w.Description,
            nodes = ParseNodes(w.NodesJson),
            status = w.Status
        }).ToList();
    }

    /// <summary>发起审批：按流程定义生成实例和第一个待办任务（tenantId 显式落库，保证租户隔离）</summary>
    public async Task<string> SubmitAsync(WorkflowSubmitRequest req, long applicantId, string applicantName, long tenantId = 0)
    {
        if (string.IsNullOrWhiteSpace(req.workflowCode) || string.IsNullOrWhiteSpace(req.bizTitle))
            throw new DomainException("流程编码与业务标题不能为空", 400);

        var wf = await _db.Queryable<SysWorkflow>()
            .FirstAsync(w => w.WorkflowCode == req.workflowCode && w.Status == 1 && w.IsDeleted == false)
            ?? throw new DomainException("流程不存在或已停用", 404);

        var nodes = ParseNodes(wf.NodesJson);
        if (nodes.Count == 0)
            throw new DomainException("流程未配置审批节点", 400);

        var instance = new SysWorkflowInstance
        {
            Id = NewId(),
            WorkflowId = wf.Id,
            WorkflowName = wf.WorkflowName,
            BizTitle = req.bizTitle,
            Remark = req.remark,
            ApplicantId = applicantId,
            ApplicantName = applicantName,
            TenantId = tenantId,
            CurrentNodeIndex = 0,
            Status = 1
        };
        var firstTask = new SysWorkflowTask
        {
            Id = NewId(),
            InstanceId = instance.Id,
            NodeName = nodes[0].name,
            NodeIndex = 0,
            AssigneeRoleCode = nodes[0].approverRole,
            TenantId = tenantId,
            Status = 0
        };
        await _db.Insertable(instance).ExecuteCommandAsync();
        await _db.Insertable(firstTask).ExecuteCommandAsync();
        return instance.Id.ToString();
    }

    /// <summary>待办任务：拥有对应审批角色的当前用户可看到的任务（超管看全部；非超管仅限平台公共 + 本租户任务）</summary>
    public async Task<List<WorkflowTaskDto>> GetMyTasksAsync(long userId, List<string> roleCodes, bool isSuperAdmin, long tenantId = 0)
    {
        var tasks = await _db.Queryable<SysWorkflowTask>()
            .Where(t => t.Status == 0 && t.IsDeleted == false)
            .ToListAsync();
        if (!isSuperAdmin)
            tasks = tasks.Where(t => roleCodes.Contains(t.AssigneeRoleCode)
                && (t.TenantId == 0 || t.TenantId == tenantId)).ToList();
        return await ToDtos(tasks);
    }

    /// <summary>审批：通过则推进到下一节点（或结束），驳回则终止整个流程</summary>
    public async Task ApproveAsync(long taskId, WorkflowApproveRequest req, long handlerUserId, string handlerName, List<string> roleCodes, bool isSuperAdmin, long tenantId = 0)
    {
        var task = await _db.Queryable<SysWorkflowTask>().FirstAsync(t => t.Id == taskId && t.IsDeleted == false)
            ?? throw new DomainException("任务不存在", 404);
        if (task.Status != 0)
            throw new DomainException("该任务已处理，请勿重复操作", 400);
        if (!isSuperAdmin && !roleCodes.Contains(task.AssigneeRoleCode))
            throw new DomainException("无权审批该任务（需要角色：" + task.AssigneeRoleCode + "）", 403);
        if (!isSuperAdmin && task.TenantId != 0 && task.TenantId != tenantId)
            throw new DomainException("无权审批其他租户的任务", 403);

        var instance = await _db.Queryable<SysWorkflowInstance>().FirstAsync(i => i.Id == task.InstanceId)
            ?? throw new DomainException("流程实例不存在", 404);
        var nodes = ParseNodes((await _db.Queryable<SysWorkflow>().FirstAsync(w => w.Id == instance.WorkflowId))?.NodesJson);

        task.Status = req.approved ? 1 : 2;
        task.Comment = req.comment;
        task.HandlerName = handlerName;
        task.HandleTime = DateTime.Now;
        task.UpdatedAt = DateTime.Now;
        await _db.Updateable(task).ExecuteCommandAsync();

        if (!req.approved)
        {
            // 驳回：流程终止
            instance.Status = 3;
            instance.FinishedAt = DateTime.Now;
            instance.UpdatedAt = DateTime.Now;
            await _db.Updateable(instance).ExecuteCommandAsync();
            return;
        }

        var nextIndex = task.NodeIndex + 1;
        if (nextIndex >= nodes.Count)
        {
            // 最后一级通过：流程完成
            instance.Status = 2;
            instance.FinishedAt = DateTime.Now;
            instance.UpdatedAt = DateTime.Now;
            await _db.Updateable(instance).ExecuteCommandAsync();
        }
        else
        {
            // 推进下一节点，生成新的待办任务
            instance.CurrentNodeIndex = nextIndex;
            instance.UpdatedAt = DateTime.Now;
            await _db.Updateable(instance).ExecuteCommandAsync();
            await _db.Insertable(new SysWorkflowTask
            {
                Id = NewId(),
                InstanceId = instance.Id,
                NodeName = nodes[nextIndex].name,
                NodeIndex = nextIndex,
                AssigneeRoleCode = nodes[nextIndex].approverRole,
                TenantId = task.TenantId != 0 ? task.TenantId : tenantId,
                Status = 0
            }).ExecuteCommandAsync();
        }
    }

    /// <summary>我的/全部流程实例分页（非超管只见自己发起的）</summary>
    public async Task<PageResult<WorkflowInstanceDto>> GetInstancesAsync(int pageIndex, int pageSize, long userId, bool isSuperAdmin, int? status = null)
    {
        var query = _db.Queryable<SysWorkflowInstance>().Where(i => i.IsDeleted == false);
        if (!isSuperAdmin)
            query = query.Where(i => i.ApplicantId == userId);
        if (status.HasValue)
            query = query.Where(i => i.Status == status);

        var total = await query.CountAsync();
        var list = await query.OrderByDescending(i => i.CreatedAt)
            .Skip((pageIndex - 1) * pageSize).Take(pageSize).ToListAsync();
        var nodesByWf = (await _db.Queryable<SysWorkflow>().ToListAsync())
            .ToDictionary(w => w.Id, w => ParseNodes(w.NodesJson));

        var items = list.Select(i =>
        {
            nodesByWf.TryGetValue(i.WorkflowId, out var nodes);
            var node = nodes is { Count: > 0 } ? nodes[Math.Min(i.CurrentNodeIndex, nodes.Count - 1)] : null;
            return new WorkflowInstanceDto
            {
                id = i.Id.ToString(),
                workflowName = i.WorkflowName,
                bizTitle = i.BizTitle,
                remark = i.Remark,
                applicantName = i.ApplicantName,
                currentNode = node?.name ?? "",
                status = i.Status,
                statusText = StatusText(i.Status),
                finishedAt = i.FinishedAt,
                createdAt = i.CreatedAt
            };
        }).ToList();
        return new PageResult<WorkflowInstanceDto>(total, pageIndex, pageSize, items);
    }

    /// <summary>实例详情（含全部任务历史）</summary>
    public async Task<(WorkflowInstanceDto Instance, List<WorkflowTaskDto> Tasks)> GetDetailAsync(long instanceId)
    {
        var i = await _db.Queryable<SysWorkflowInstance>().FirstAsync(x => x.Id == instanceId && x.IsDeleted == false)
            ?? throw new DomainException("流程实例不存在", 404);
        var tasks = await _db.Queryable<SysWorkflowTask>()
            .Where(t => t.InstanceId == instanceId).OrderBy(t => t.NodeIndex).ToListAsync();
        var dto = new WorkflowInstanceDto
        {
            id = i.Id.ToString(), workflowName = i.WorkflowName, bizTitle = i.BizTitle,
            remark = i.Remark, applicantName = i.ApplicantName, status = i.Status,
            statusText = StatusText(i.Status), finishedAt = i.FinishedAt, createdAt = i.CreatedAt
        };
        return (dto, await ToDtos(tasks));
    }

    private async Task<List<WorkflowTaskDto>> ToDtos(List<SysWorkflowTask> tasks)
    {
        var instanceIds = tasks.Select(t => t.InstanceId).Distinct().ToList();
        var instances = instanceIds.Count == 0
            ? new List<SysWorkflowInstance>()
            : await _db.Queryable<SysWorkflowInstance>().Where(i => instanceIds.Contains(i.Id)).ToListAsync();
        var instMap = instances.ToDictionary(i => i.Id);
        return tasks.Select(t =>
        {
            instMap.TryGetValue(t.InstanceId, out var inst);
            return new WorkflowTaskDto
            {
                id = t.Id.ToString(),
                instanceId = t.InstanceId.ToString(),
                bizTitle = inst?.BizTitle ?? "",
                workflowName = inst?.WorkflowName ?? "",
                nodeName = t.NodeName,
                applicantName = inst?.ApplicantName ?? "",
                status = t.Status,
                comment = t.Comment,
                handlerName = t.HandlerName,
                handleTime = t.HandleTime,
                createdAt = t.CreatedAt
            };
        }).ToList();
    }

    private static List<WorkflowNodeDto> ParseNodes(string? nodesJson)
    {
        if (string.IsNullOrEmpty(nodesJson)) return new();
        try { return JsonSerializer.Deserialize<List<WorkflowNodeDto>>(nodesJson) ?? new(); }
        catch { return new(); }
    }
}

// ==================== 租户 & 消息 ====================

/// <summary>租户管理应用服务：租户列表与启停维护（平台管理员用）</summary>
public class TenantAppService
{
    private readonly ISqlSugarClient _db;
    private readonly Common.Snowflake.SnowflakeIdGenerator _snowflake;
    public TenantAppService(ISqlSugarClient db, Common.Snowflake.SnowflakeIdGenerator snowflake) { _db = db; _snowflake = snowflake; }

    /// <summary>租户列表（超管接口，不受租户过滤影响：平台层 CurrentId=0 不过滤）</summary>
    public async Task<List<SysTenant>> ListAsync()
        => await _db.Queryable<SysTenant>().Where(t => t.IsDeleted == false).OrderBy(t => t.Id).ToListAsync();

    /// <summary>新增/修改租户</summary>
    public async Task SaveAsync(long id, string tenantName, string tenantCode, string? contact, string? remark, int status)
    {
        if (string.IsNullOrWhiteSpace(tenantName) || string.IsNullOrWhiteSpace(tenantCode))
            throw new DomainException("租户名称与编码不能为空", 400);
        if (id == 0)
        {
            if (await _db.Queryable<SysTenant>().AnyAsync(t => t.TenantCode == tenantCode && t.IsDeleted == false))
                throw new DomainException("租户编码已存在", 400);
            await _db.Insertable(new SysTenant
            {
                Id = _snowflake.NextId(), TenantName = tenantName, TenantCode = tenantCode,
                Contact = contact, Remark = remark, Status = status
            }).ExecuteCommandAsync();
        }
        else
        {
            var t = await _db.Queryable<SysTenant>().FirstAsync(x => x.Id == id) ?? throw new DomainException("租户不存在", 404);
            t.TenantName = tenantName; t.TenantCode = tenantCode; t.Contact = contact; t.Remark = remark; t.Status = status;
            t.UpdatedAt = DateTime.Now;
            await _db.Updateable(t).ExecuteCommandAsync();
        }
    }
}

/// <summary>站内消息应用服务：消息列表、标记已读、队列演示（手动发布/查看队列状态）</summary>
public class MessageAppService
{
    private readonly ISqlSugarClient _db;
    private readonly Common.Messaging.IMessageQueue _mq;
    public MessageAppService(ISqlSugarClient db, Common.Messaging.IMessageQueue mq) { _db = db; _mq = mq; }

    /// <summary>消息分页（超管看全部，普通用户看系统消息 + 发给自己的）</summary>
    public async Task<(int Total, List<SysMessage> Items)> PageAsync(int pageIndex, int pageSize, long userId, bool isSuperAdmin)
    {
        var query = _db.Queryable<SysMessage>().Where(m => m.IsDeleted == false);
        if (!isSuperAdmin)
            query = query.Where(m => m.ReceiverId == 0 || m.ReceiverId == userId);
        var total = await query.CountAsync();
        var items = await query.OrderByDescending(m => m.CreatedAt)
            .Skip((pageIndex - 1) * pageSize).Take(pageSize).ToListAsync();
        return (total, items);
    }

    /// <summary>标记已读</summary>
    public async Task MarkReadAsync(long id)
    {
        var m = await _db.Queryable<SysMessage>().FirstAsync(x => x.Id == id) ?? throw new DomainException("消息不存在", 404);
        m.IsRead = true; m.UpdatedAt = DateTime.Now;
        await _db.Updateable(m).ExecuteCommandAsync();
    }

    /// <summary>消息队列演示：向任意主题发布一条测试消息</summary>
    public async Task PublishDemoAsync(string topic, string content)
    {
        if (string.IsNullOrWhiteSpace(topic)) topic = "demo.test";
        await _mq.PublishAsync(topic, System.Text.Json.JsonSerializer.Serialize(new
        {
            source = "manual", content, publisher = "演示接口", at = DateTime.Now
        }));
    }

    /// <summary>队列状态：提供方与各主题积压</summary>
    public object GetStats() => new
    {
        provider = _mq.ProviderName,
        queues = _mq.GetStats().Select(kv => new { topic = kv.Key, backlog = kv.Value }).ToList()
    };
}
