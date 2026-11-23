namespace TaoAdmin.Common.Auth;

/// <summary>
/// 登录失败锁定账本（读侧契约）：失败计数、锁定状态、剩余锁定时间。
/// 键=用户名，窗口内连续失败计数；密码正确即清零；锁定到期自动解锁。
/// </summary>
public interface ILoginLockoutStore
{
    /// <summary>是否锁定中；锁定时返回剩余分钟（向上取整），未锁定返回 null。</summary>
    int? GetRemainingLockMinutes(string username, int lockMinutes);

    /// <summary>记一次失败；达阈值即锁定（锁定起点=now，时长=lockMinutes）。</summary>
    void RecordFailure(string username, int maxFails, int lockMinutes);

    /// <summary>清零（登录成功或管理员手工解锁）。</summary>
    void Clear(string username);

    /// <summary>当前窗口内连续失败次数（无记录=0；供验证码触发阈值判断）。</summary>
    int GetFails(string username);

    /// <summary>测试与运维用：整账清空。</summary>
    void Reset();
}
