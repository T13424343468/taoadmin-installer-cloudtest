namespace TaoAdmin.Common.Http;

/// <summary>分页查询基类请求</summary>
public class PageRequest
{
    /// <summary>页码，从 1 开始</summary>
    public int PageIndex { get; set; } = 1;
    /// <summary>每页条数，默认 10，最大 200</summary>
    public int PageSize { get; set; } = 10;

    /// <summary>跳过条数（供 SqlSugar 使用）</summary>
    public int Skip => Math.Max(0, (PageIndex - 1) * PageSize);
}

/// <summary>分页返回结果</summary>
public class PageResult<T>
{
    /// <summary>总记录数</summary>
    public long Total { get; set; }
    /// <summary>页码</summary>
    public int PageIndex { get; set; }
    /// <summary>每页条数</summary>
    public int PageSize { get; set; }
    /// <summary>当前页数据</summary>
    public List<T> Items { get; set; } = new();
    /// <summary>下一页游标（亿级深翻页用：下一页请求带上它可恒定速度定位；为空表示没有下一页或未启用游标）</summary>
    public string? NextCursor { get; set; }

    public PageResult() { }

    public PageResult(long total, int pageIndex, int pageSize, List<T> items)
    {
        Total = total;
        PageIndex = pageIndex;
        PageSize = pageSize;
        Items = items;
    }

    public PageResult(long total, int pageIndex, int pageSize, List<T> items, string? nextCursor)
        : this(total, pageIndex, pageSize, items)
    {
        NextCursor = nextCursor;
    }
}
