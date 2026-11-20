namespace TaoAdmin.Common.Entities;

/// <summary>
/// 多租户实体接口：实现本接口的实体自动接入租户过滤——
/// 查询按当前租户过滤（平台层 TenantId=0 不过滤），写入自动补齐租户 ID。
/// </summary>
public interface ITenantEntity
{
    /// <summary>所属租户 ID（0 = 平台层 / 公共数据）</summary>
    long TenantId { get; set; }
}
