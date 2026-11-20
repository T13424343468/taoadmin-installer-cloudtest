using SqlSugar;

namespace TaoAdmin.Common.SqlSugar;

/// <summary>
/// 读写分离辅助扩展。
/// </summary>
/// <remarks>
/// 配置从库后（ConnectionStrings:Slaves + ReadWriteSplitting:Enabled），
/// 普通查询自动走从库（存在毫秒~秒级主从复制延迟）。
/// 「写入后立即查询」的场景（如新增用户后马上返回列表）如需读到最新数据，
/// 用 <c>db.ForceMaster(() =&gt; ...)</c> 把这段查询固定到主库。
/// </remarks>
public static class ReadWriteExtensions
{
    /// <summary>
    /// 在回调内强制走主库（临时禁用读写分离路由），执行完自动恢复。
    /// 典型用法：
    /// <code>
    /// var user = db.ForceMaster(() =&gt;
    ///     db.Queryable&lt;SysUser&gt;().First(u =&gt; u.UserName == name));
    /// </code>
    /// </summary>
    public static T ForceMaster<T>(this ISqlSugarClient db, Func<T> action)
    {
        var previous = db.Ado.IsDisableMasterSlaveSeparation;
        db.Ado.IsDisableMasterSlaveSeparation = true;
        try
        {
            return action();
        }
        finally
        {
            db.Ado.IsDisableMasterSlaveSeparation = previous;
        }
    }

    /// <summary>
    /// 无返回值版本：<c>db.ForceMaster(() =&gt; { ...查询... });</c>
    /// </summary>
    public static void ForceMaster(this ISqlSugarClient db, Action action)
    {
        var previous = db.Ado.IsDisableMasterSlaveSeparation;
        db.Ado.IsDisableMasterSlaveSeparation = true;
        try
        {
            action();
        }
        finally
        {
            db.Ado.IsDisableMasterSlaveSeparation = previous;
        }
    }
}
