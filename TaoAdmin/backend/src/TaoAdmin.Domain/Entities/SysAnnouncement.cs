using TaoAdmin.Common.Entities;
using SqlSugar;

namespace TaoAdmin.Domain.Entities;

/// <summary>
/// 公告通知表（sys_announcement）：管理员发布、可上下线，登录用户可见最新已发布公告。
/// </summary>
[SugarTable("sys_announcement", TableDescription = "公告通知")]
public class SysAnnouncement : BaseEntity
{
    /// <summary>公告标题</summary>
    [SugarColumn(Length = 100, ColumnDescription = "公告标题")]
    public string Title { get; set; } = string.Empty;

    /// <summary>公告内容</summary>
    [SugarColumn(ColumnDataType = "TEXT", IsNullable = true, ColumnDescription = "公告内容")]
    public string? Content { get; set; }

    /// <summary>状态：0 已下线，1 已发布（上线）</summary>
    [SugarColumn(ColumnDescription = "状态 0下线 1发布")]
    public int Status { get; set; } = 1;

    /// <summary>发布人（显示名）</summary>
    [SugarColumn(Length = 50, IsNullable = true, ColumnDescription = "发布人")]
    public string? PublisherName { get; set; }

    /// <summary>发布时间</summary>
    [SugarColumn(IsNullable = true, ColumnDescription = "发布时间")]
    public DateTime? PublishTime { get; set; }
}
