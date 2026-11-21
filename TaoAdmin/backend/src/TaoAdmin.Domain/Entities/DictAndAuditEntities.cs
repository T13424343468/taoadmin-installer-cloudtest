using TaoAdmin.Common.Entities;
using SqlSugar;

namespace TaoAdmin.Domain.Entities;

/// <summary>
/// 字典类型表（sys_dict_type）：业务下拉框/状态枚举的分类定义，如「性别」「审批状态」。
/// </summary>
[SugarTable("sys_dict_type", TableDescription = "字典类型")]
public class SysDictType : BaseEntity, TaoAdmin.Common.Entities.ITenantEntity
{
    /// <summary>字典名称（如：性别）</summary>
    [SugarColumn(Length = 50, ColumnDescription = "字典名称")]
    public string DictName { get; set; } = string.Empty;

    /// <summary>字典编码（唯一，如：gender）</summary>
    [SugarColumn(Length = 50, ColumnDescription = "字典编码")]
    public string DictCode { get; set; } = string.Empty;

    /// <summary>状态：0 停用，1 启用</summary>
    [SugarColumn(ColumnDescription = "状态 0停用 1启用")]
    public int Status { get; set; } = 1;

    /// <summary>备注</summary>
    [SugarColumn(Length = 200, IsNullable = true, ColumnDescription = "备注")]
    public string? Remark { get; set; }

    /// <summary>所属租户（0 = 平台公共字典）</summary>
    [SugarColumn(ColumnDescription = "所属租户ID")]
    public long TenantId { get; set; }
}

/// <summary>
/// 字典数据表（sys_dict_data）：字典类型下的明细项，如「性别 → 男/女/未知」。
/// </summary>
[SugarTable("sys_dict_data", TableDescription = "字典数据")]
public class SysDictData : BaseEntity, TaoAdmin.Common.Entities.ITenantEntity
{
    /// <summary>所属字典编码</summary>
    [SugarColumn(Length = 50, ColumnDescription = "字典编码")]
    public string DictCode { get; set; } = string.Empty;

    /// <summary>显示文本（如：男）</summary>
    [SugarColumn(Length = 50, ColumnDescription = "显示文本")]
    public string Label { get; set; } = string.Empty;

    /// <summary>存储值（如：M）</summary>
    [SugarColumn(Length = 50, ColumnDescription = "存储值")]
    public string Value { get; set; } = string.Empty;

    /// <summary>排序号（越小越靠前）</summary>
    [SugarColumn(ColumnDescription = "排序号")]
    public int Sort { get; set; }

    /// <summary>状态：0 停用，1 启用</summary>
    [SugarColumn(ColumnDescription = "状态 0停用 1启用")]
    public int Status { get; set; } = 1;

    /// <summary>备注（可用于存扩展属性，如颜色）</summary>
    [SugarColumn(Length = 200, IsNullable = true, ColumnDescription = "备注")]
    public string? Remark { get; set; }

    /// <summary>所属租户（0 = 平台公共字典）</summary>
    [SugarColumn(ColumnDescription = "所属租户ID")]
    public long TenantId { get; set; }
}

/// <summary>
/// 操作日志表（sys_operation_log）：记录所有写操作的审计轨迹，登录成功/失败也记录在内。
/// </summary>
[SugarTable("sys_operation_log", TableDescription = "操作日志")]
[SugarIndex("idx_oplog_created_id", nameof(BaseEntity.CreatedAt), OrderByType.Asc, nameof(BaseEntity.Id), OrderByType.Desc)]
[SugarIndex("idx_oplog_user_created", nameof(UserName), OrderByType.Asc, nameof(BaseEntity.CreatedAt), OrderByType.Asc)]
[SugarIndex("idx_oplog_category_created", nameof(Category), OrderByType.Asc, nameof(BaseEntity.CreatedAt), OrderByType.Asc)]
public class SysOperationLog : BaseEntity
{
    /// <summary>操作人 ID（0 = 未登录/登录失败）</summary>
    [SugarColumn(ColumnDescription = "操作人ID")]
    public long UserId { get; set; }

    /// <summary>操作人账号（登录失败时记录尝试的账号）</summary>
    [SugarColumn(Length = 50, ColumnDescription = "操作人账号")]
    public string UserName { get; set; } = string.Empty;

    /// <summary>所属租户（0 = 平台）</summary>
    [SugarColumn(ColumnDescription = "所属租户ID")]
    public long TenantId { get; set; }

    /// <summary>日志类别：login 登录 / operation 操作</summary>
    [SugarColumn(Length = 20, ColumnDescription = "类别 login/operation")]
    public string Category { get; set; } = "operation";

    /// <summary>请求方法（GET/POST/...）</summary>
    [SugarColumn(Length = 10, ColumnDescription = "请求方法")]
    public string Method { get; set; } = string.Empty;

    /// <summary>请求路径（如 /api/identity/users）</summary>
    [SugarColumn(Length = 200, ColumnDescription = "请求路径")]
    public string Path { get; set; } = string.Empty;

    /// <summary>查询字符串</summary>
    [SugarColumn(Length = 500, IsNullable = true, ColumnDescription = "查询字符串")]
    public string? QueryString { get; set; }

    /// <summary>HTTP 状态码</summary>
    [SugarColumn(ColumnDescription = "HTTP状态码")]
    public int StatusCode { get; set; }

    /// <summary>业务结果：1 成功，0 失败（HTTP 4xx/5xx 或业务失败）</summary>
    [SugarColumn(ColumnDescription = "结果 1成功 0失败")]
    public int Success { get; set; }

    /// <summary>客户端 IP</summary>
    [SugarColumn(Length = 50, ColumnDescription = "客户端IP")]
    public string Ip { get; set; } = string.Empty;

    /// <summary>耗时（毫秒）</summary>
    [SugarColumn(ColumnDescription = "耗时毫秒")]
    public int ElapsedMs { get; set; }

    /// <summary>TraceId（与网关链路追踪对应）</summary>
    [SugarColumn(Length = 50, IsNullable = true, ColumnDescription = "链路追踪ID")]
    public string? TraceId { get; set; }

    /// <summary>敏感操作前后值快照（JSON，密码类字段一律掩码 ***；可空=普通日志）</summary>
    [SugarColumn(ColumnDataType = "longtext", IsNullable = true, ColumnDescription = "关键数据留痕JSON")]
    public string? Detail { get; set; }
}
