using TaoAdmin.Common.Entities;
using SqlSugar;

namespace TaoAdmin.Domain.Entities;

/// <summary>
/// 工作流定义（sys_workflow）：审批流模板。
/// 节点存在 NodesJson，形如：
/// [ {"name":"组长审批","approverRole":"group_leader"}, {"name":"经理审批","approverRole":"manager"} ]
/// 顺序即审批顺序，最后一步通过后整个流程完成。
/// </summary>
[SugarTable("sys_workflow", TableDescription = "工作流定义")]
public class SysWorkflow : BaseEntity, TaoAdmin.Common.Entities.ITenantEntity
{
    /// <summary>流程名称（如：请假审批）</summary>
    [SugarColumn(Length = 50, ColumnDescription = "流程名称")]
    public string WorkflowName { get; set; } = string.Empty;

    /// <summary>流程编码（唯一，如 leave_audit）</summary>
    [SugarColumn(Length = 50, ColumnDescription = "流程编码")]
    public string WorkflowCode { get; set; } = string.Empty;

    /// <summary>说明</summary>
    [SugarColumn(Length = 200, IsNullable = true, ColumnDescription = "流程说明")]
    public string? Description { get; set; }

    /// <summary>审批节点定义（JSON 数组）</summary>
    [SugarColumn(ColumnDataType = "text", IsNullable = true, ColumnDescription = "节点定义JSON")]
    public string? NodesJson { get; set; }

    /// <summary>状态：0 停用，1 启用</summary>
    [SugarColumn(ColumnDescription = "状态 0停用 1启用")]
    public int Status { get; set; } = 1;

    /// <summary>所属租户（0 = 平台通用模板）</summary>
    [SugarColumn(ColumnDescription = "所属租户ID")]
    public long TenantId { get; set; }
}

/// <summary>
/// 工作流实例（sys_workflow_instance）：一次具体的审批。
/// 状态：1 进行中，2 已通过，3 已驳回，4 已取消。
/// </summary>
[SugarTable("sys_workflow_instance", TableDescription = "工作流实例")]
public class SysWorkflowInstance : BaseEntity, TaoAdmin.Common.Entities.ITenantEntity
{
    /// <summary>所属流程定义</summary>
    [SugarColumn(ColumnDescription = "流程定义ID")]
    public long WorkflowId { get; set; }

    /// <summary>流程名称（冗余，便于查询展示）</summary>
    [SugarColumn(Length = 50, ColumnDescription = "流程名称")]
    public string WorkflowName { get; set; } = string.Empty;

    /// <summary>业务标题（如：张三的请假申请）</summary>
    [SugarColumn(Length = 100, ColumnDescription = "业务标题")]
    public string BizTitle { get; set; } = string.Empty;

    /// <summary>业务说明</summary>
    [SugarColumn(Length = 500, IsNullable = true, ColumnDescription = "业务说明")]
    public string? Remark { get; set; }

    /// <summary>发起人</summary>
    [SugarColumn(ColumnDescription = "发起人ID")]
    public long ApplicantId { get; set; }

    /// <summary>发起人姓名</summary>
    [SugarColumn(Length = 50, ColumnDescription = "发起人姓名")]
    public string ApplicantName { get; set; } = string.Empty;

    /// <summary>当前节点下标（从 0 开始）</summary>
    [SugarColumn(ColumnDescription = "当前节点下标")]
    public int CurrentNodeIndex { get; set; }

    /// <summary>状态：1 进行中 2 已通过 3 已驳回 4 已取消</summary>
    [SugarColumn(ColumnDescription = "状态 1进行中 2通过 3驳回 4取消")]
    public int Status { get; set; } = 1;

    /// <summary>完成时间</summary>
    [SugarColumn(IsNullable = true, ColumnDescription = "完成时间")]
    public DateTime? FinishedAt { get; set; }

    /// <summary>所属租户</summary>
    [SugarColumn(ColumnDescription = "所属租户ID")]
    public long TenantId { get; set; }
}

/// <summary>
/// 工作流任务（sys_workflow_task）：每个审批节点生成一条任务。
/// 任务状态：0 待审批，1 已通过，2 已驳回。
/// </summary>
[SugarTable("sys_workflow_task", TableDescription = "工作流任务")]
public class SysWorkflowTask : BaseEntity, TaoAdmin.Common.Entities.ITenantEntity
{
    /// <summary>所属流程实例</summary>
    [SugarColumn(ColumnDescription = "流程实例ID")]
    public long InstanceId { get; set; }

    /// <summary>节点名称（如：组长审批）</summary>
    [SugarColumn(Length = 50, ColumnDescription = "节点名称")]
    public string NodeName { get; set; } = string.Empty;

    /// <summary>节点下标</summary>
    [SugarColumn(ColumnDescription = "节点下标")]
    public int NodeIndex { get; set; }

    /// <summary>审批角色编码（该角色的人可审批）</summary>
    [SugarColumn(Length = 50, ColumnDescription = "审批角色编码")]
    public string AssigneeRoleCode { get; set; } = string.Empty;

    /// <summary>任务状态：0 待审批 1 通过 2 驳回</summary>
    [SugarColumn(ColumnDescription = "任务状态 0待审批 1通过 2驳回")]
    public int Status { get; set; }

    /// <summary>审批意见</summary>
    [SugarColumn(Length = 500, IsNullable = true, ColumnDescription = "审批意见")]
    public string? Comment { get; set; }

    /// <summary>审批人姓名</summary>
    [SugarColumn(Length = 50, IsNullable = true, ColumnDescription = "审批人姓名")]
    public string? HandlerName { get; set; }

    /// <summary>处理时间</summary>
    [SugarColumn(IsNullable = true, ColumnDescription = "处理时间")]
    public DateTime? HandleTime { get; set; }

    /// <summary>所属租户</summary>
    [SugarColumn(ColumnDescription = "所属租户ID")]
    public long TenantId { get; set; }
}
