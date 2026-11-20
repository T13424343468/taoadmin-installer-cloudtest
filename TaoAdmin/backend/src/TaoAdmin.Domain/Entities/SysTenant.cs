using TaoAdmin.Common.Entities;
using SqlSugar;

namespace TaoAdmin.Domain.Entities;

/// <summary>租户表（sys_tenant）：多租户的组织/分公司定义</summary>
[SugarTable("sys_tenant", TableDescription = "系统租户")]
public class SysTenant : BaseEntity
{
    /// <summary>租户名称（如：华东分公司）</summary>
    [SugarColumn(Length = 50, ColumnDescription = "租户名称")]
    public string TenantName { get; set; } = string.Empty;

    /// <summary>租户编码（唯一，如 east）</summary>
    [SugarColumn(Length = 50, ColumnDescription = "租户编码")]
    public string TenantCode { get; set; } = string.Empty;

    /// <summary>状态：0 停用，1 启用</summary>
    [SugarColumn(ColumnDescription = "状态 0停用 1启用")]
    public int Status { get; set; } = 1;

    /// <summary>联系人</summary>
    [SugarColumn(Length = 50, IsNullable = true, ColumnDescription = "联系人")]
    public string? Contact { get; set; }

    /// <summary>备注</summary>
    [SugarColumn(Length = 200, IsNullable = true, ColumnDescription = "备注")]
    public string? Remark { get; set; }
}

/// <summary>站内消息表（sys_message）：消息队列消费者的落地示例</summary>
[SugarTable("sys_message", TableDescription = "站内消息")]
public class SysMessage : BaseEntity
{
    /// <summary>消息标题</summary>
    [SugarColumn(Length = 100, ColumnDescription = "消息标题")]
    public string Title { get; set; } = string.Empty;

    /// <summary>消息内容</summary>
    [SugarColumn(Length = 500, IsNullable = true, ColumnDescription = "消息内容")]
    public string? Content { get; set; }

    /// <summary>来源主题（如 user.created）</summary>
    [SugarColumn(Length = 50, ColumnDescription = "来源主题")]
    public string Topic { get; set; } = string.Empty;

    /// <summary>接收人 ID（0 = 全员/系统）</summary>
    [SugarColumn(ColumnDescription = "接收人ID 0为系统")]
    public long ReceiverId { get; set; }

    /// <summary>已读：true 已读</summary>
    [SugarColumn(ColumnDescription = "是否已读")]
    public bool IsRead { get; set; }
}
